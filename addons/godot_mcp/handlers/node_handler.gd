@tool
extends Node

func _get_plugin() -> EditorPlugin:
	return Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null

func _get_edited_scene_root() -> Node:
	var plugin = _get_plugin()
	if not plugin:
		return null
	var editor_interface = plugin.get_editor_interface()
	return editor_interface.get_edited_scene_root()

func _get_undo_redo() -> EditorUndoRedoManager:
	var plugin = _get_plugin()
	if not plugin:
		return null
	return plugin.get_undo_redo()

func _find_node_by_path(root: Node, path: String) -> Node:
	if path == "." or path == root.name:
		return root
	if path.begins_with("./"):
		path = path.substr(2)
	return root.get_node_or_null(path)

func handle_add(params: Dictionary) -> Dictionary:
	var parent_path = params.get("parent_path", ".")
	var node_type = params.get("node_type", "Node")
	var node_name = params.get("node_name", "NewNode")
	var properties = params.get("properties", {})

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var parent = _find_node_by_path(root, parent_path)
	if not parent:
		return {"error": "Parent node not found: %s" % parent_path}

	# Create the new node
	var new_node: Node = null
	if ClassDB.class_exists(node_type):
		new_node = ClassDB.instantiate(node_type)
	else:
		return {"error": "Invalid node type: %s" % node_type}

	new_node.name = node_name

	# Set properties with validation
	var valid_props = _get_property_names(new_node)
	for prop_name in properties.keys():
		if not prop_name in valid_props:
			new_node.queue_free()
			return {"error": "Property '%s' does not exist on node type %s" % [prop_name, node_type]}
		var value = properties[prop_name]
		value = _deserialize_value(value)
		new_node.set(prop_name, value)

	# Add to parent with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Add Node: %s" % node_name)
		undo_redo.add_do_method(parent, "add_child", new_node)
		undo_redo.add_do_method(new_node, "set", "owner", root)
		undo_redo.add_do_reference(new_node)
		undo_redo.add_undo_method(parent, "remove_child", new_node)
		undo_redo.commit_action()
	else:
		parent.add_child(new_node)
		new_node.owner = root

	return {"result": {
		"node_path": str(new_node.get_path()),
		"node_type": node_type,
		"node_name": new_node.name
	}}

func handle_modify(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var properties = params.get("properties", {})

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	# Validate properties first
	var valid_props = _get_property_names(node)
	for prop_name in properties.keys():
		if not prop_name in valid_props:
			return {"error": "Property '%s' does not exist on node type %s" % [prop_name, node.get_class()]}

	# Set properties with undo/redo support
	var undo_redo = _get_undo_redo()
	var modified_props = []

	if undo_redo:
		undo_redo.create_action("Modify Node: %s" % node.name)
		for prop_name in properties.keys():
			var old_value = node.get(prop_name)
			var new_value = _deserialize_value(properties[prop_name])
			undo_redo.add_do_property(node, prop_name, new_value)
			undo_redo.add_undo_property(node, prop_name, old_value)
			modified_props.append(prop_name)
		undo_redo.commit_action()
	else:
		for prop_name in properties.keys():
			var value = _deserialize_value(properties[prop_name])
			node.set(prop_name, value)
			modified_props.append(prop_name)

	return {"result": {
		"node_path": str(node.get_path()),
		"modified_properties": modified_props
	}}

func handle_delete(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	if node_path == "." or node_path == root.name:
		return {"error": "Cannot delete the root node"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var deleted_name = node.name
	var parent = node.get_parent()
	var node_index = node.get_index()

	# Delete with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Delete Node: %s" % deleted_name)
		undo_redo.add_do_method(parent, "remove_child", node)
		undo_redo.add_undo_method(parent, "add_child", node)
		undo_redo.add_undo_method(parent, "move_child", node, node_index)
		undo_redo.add_undo_method(node, "set", "owner", root)
		undo_redo.add_undo_reference(node)
		undo_redo.commit_action()
	else:
		node.queue_free()

	return {"result": {
		"deleted_node": deleted_name,
		"success": true
	}}

func handle_get_info(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", ".")
	var include_children = params.get("include_children", false)

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var info = {
		"name": node.name,
		"type": node.get_class(),
		"path": str(node.get_path()),
		"properties": {}
	}

	# Get script
	var script = node.get_script()
	if script:
		info["script"] = script.resource_path

	# Get editable properties
	for prop in node.get_property_list():
		var prop_name = prop["name"]
		if prop_name.begins_with("_") or prop_name in ["script"]:
			continue
		if prop["usage"] & PROPERTY_USAGE_EDITOR:
			var value = node.get(prop_name)
			if value != null:
				info["properties"][prop_name] = _serialize_value(value)

	# Get children if requested
	if include_children:
		var children = []
		for child in node.get_children():
			children.append({
				"name": child.name,
				"type": child.get_class(),
				"path": str(child.get_path())
			})
		info["children"] = children

	return {"result": info}

func handle_rename(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var new_name = params.get("new_name", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if new_name.is_empty():
		return {"error": "New name is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var old_name = node.name

	# Rename with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Rename Node: %s → %s" % [old_name, new_name])
		undo_redo.add_do_property(node, "name", new_name)
		undo_redo.add_undo_property(node, "name", old_name)
		undo_redo.commit_action()
	else:
		node.name = new_name

	return {"result": {
		"old_name": old_name,
		"new_name": node.name,
		"new_path": str(node.get_path())
	}}

func handle_reparent(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var new_parent_path = params.get("new_parent_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if new_parent_path.is_empty():
		return {"error": "New parent path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var new_parent = _find_node_by_path(root, new_parent_path)
	if not new_parent:
		return {"error": "New parent not found: %s" % new_parent_path}

	if node == root:
		return {"error": "Cannot reparent the root node"}

	var old_parent = node.get_parent()
	var old_index = node.get_index()

	# Reparent with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Reparent Node: %s" % node.name)
		undo_redo.add_do_method(old_parent, "remove_child", node)
		undo_redo.add_do_method(new_parent, "add_child", node)
		undo_redo.add_do_method(node, "set", "owner", root)
		undo_redo.add_undo_method(new_parent, "remove_child", node)
		undo_redo.add_undo_method(old_parent, "add_child", node)
		undo_redo.add_undo_method(old_parent, "move_child", node, old_index)
		undo_redo.add_undo_method(node, "set", "owner", root)
		undo_redo.commit_action()
	else:
		old_parent.remove_child(node)
		new_parent.add_child(node)
		node.owner = root

	return {"result": {
		"node_name": node.name,
		"old_parent": old_parent.name,
		"new_parent": new_parent.name,
		"new_path": str(node.get_path())
	}}

func handle_connect_signal(params: Dictionary) -> Dictionary:
	var source_path = params.get("source_path", "")
	var signal_name = params.get("signal_name", "")
	var target_path = params.get("target_path", "")
	var method_name = params.get("method_name", "")

	if source_path.is_empty() or target_path.is_empty():
		return {"error": "Source and target paths are required"}

	if signal_name.is_empty() or method_name.is_empty():
		return {"error": "Signal name and method name are required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var source = _find_node_by_path(root, source_path)
	if not source:
		return {"error": "Source node not found: %s" % source_path}

	var target = _find_node_by_path(root, target_path)
	if not target:
		return {"error": "Target node not found: %s" % target_path}

	if not source.has_signal(signal_name):
		return {"error": "Signal not found on source node: %s" % signal_name}

	var callable = Callable(target, method_name)

	# Connect signal with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Connect Signal: %s" % signal_name)
		undo_redo.add_do_method(source, "connect", signal_name, callable)
		undo_redo.add_undo_method(source, "disconnect", signal_name, callable)
		undo_redo.commit_action()
	else:
		var err = source.connect(signal_name, callable)
		if err != OK:
			return {"error": "Failed to connect signal: %d" % err}

	return {"result": {
		"source": str(source.get_path()),
		"signal": signal_name,
		"target": str(target.get_path()),
		"method": method_name
	}}

func handle_attach_script(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var script_path = params.get("script_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if script_path.is_empty():
		return {"error": "Script path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	if not script_path.begins_with("res://"):
		script_path = "res://" + script_path

	if not FileAccess.file_exists(script_path):
		return {"error": "Script file not found: %s" % script_path}

	var script = load(script_path)
	if not script:
		return {"error": "Failed to load script: %s" % script_path}

	var old_script = node.get_script()

	# Attach script with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Attach Script: %s" % script_path.get_file())
		undo_redo.add_do_method(node, "set_script", script)
		undo_redo.add_undo_method(node, "set_script", old_script)
		undo_redo.commit_action()
	else:
		node.set_script(script)

	return {"result": {
		"node_path": str(node.get_path()),
		"script_path": script_path
	}}

func _serialize_value(value: Variant) -> Variant:
	if value is Vector2:
		return {"x": value.x, "y": value.y}
	elif value is Vector3:
		return {"x": value.x, "y": value.y, "z": value.z}
	elif value is Color:
		return {"r": value.r, "g": value.g, "b": value.b, "a": value.a}
	elif value is Resource:
		return value.resource_path if value.resource_path else str(value)
	elif value is Node:
		return str(value.get_path())
	else:
		return value

func _deserialize_value(value: Variant) -> Variant:
	if value is Dictionary:
		if value.has("x") and value.has("y"):
			if value.has("z"):
				return Vector3(value.x, value.y, value.z)
			else:
				return Vector2(value.x, value.y)
		elif value.has("r") and value.has("g") and value.has("b"):
			var a = value.get("a", 1.0)
			return Color(value.r, value.g, value.b, a)
	return value

func _get_property_names(node: Node) -> Array:
	var names = []
	for prop in node.get_property_list():
		names.append(prop["name"])
	return names

func handle_instance(params: Dictionary) -> Dictionary:
	var scene_path = params.get("scene_path", "")
	var parent_path = params.get("parent_path", ".")
	var instance_name = params.get("name", "")

	if scene_path.is_empty():
		return {"error": "Scene path is required"}

	if not scene_path.begins_with("res://"):
		scene_path = "res://" + scene_path

	if not FileAccess.file_exists(scene_path):
		return {"error": "Scene file not found: %s" % scene_path}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var parent = _find_node_by_path(root, parent_path)
	if not parent:
		return {"error": "Parent node not found: %s" % parent_path}

	# Load and instantiate the scene
	var packed_scene = load(scene_path) as PackedScene
	if not packed_scene:
		return {"error": "Failed to load scene: %s" % scene_path}

	var instance = packed_scene.instantiate()
	if not instance:
		return {"error": "Failed to instantiate scene: %s" % scene_path}

	# Set name if provided
	if not instance_name.is_empty():
		instance.name = instance_name

	# Add to parent with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Instance Scene: %s" % instance.name)
		undo_redo.add_do_method(parent, "add_child", instance)
		undo_redo.add_do_method(instance, "set", "owner", root)
		undo_redo.add_do_method(self, "_set_owner_recursive", instance, root)
		undo_redo.add_do_reference(instance)
		undo_redo.add_undo_method(parent, "remove_child", instance)
		undo_redo.commit_action()
	else:
		parent.add_child(instance)
		instance.owner = root
		_set_owner_recursive(instance, root)

	return {"result": {
		"node_path": str(instance.get_path()),
		"node_name": instance.name,
		"scene_path": scene_path
	}}

func _set_owner_recursive(node: Node, owner: Node) -> void:
	for child in node.get_children():
		child.owner = owner
		_set_owner_recursive(child, owner)

func handle_duplicate(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var new_name = params.get("new_name", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	if node_path == "." or node_path == root.name:
		return {"error": "Cannot duplicate the root node"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	# Duplicate the node (DUPLICATE_SIGNALS | DUPLICATE_GROUPS | DUPLICATE_SCRIPTS)
	var duplicate = node.duplicate(15)
	if not duplicate:
		return {"error": "Failed to duplicate node: %s" % node_path}

	# Set name if provided
	if not new_name.is_empty():
		duplicate.name = new_name

	# Add to the same parent with undo/redo support
	var parent = node.get_parent()
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Duplicate Node: %s" % node.name)
		undo_redo.add_do_method(parent, "add_child", duplicate)
		undo_redo.add_do_method(duplicate, "set", "owner", root)
		undo_redo.add_do_method(self, "_set_owner_recursive", duplicate, root)
		undo_redo.add_do_reference(duplicate)
		undo_redo.add_undo_method(parent, "remove_child", duplicate)
		undo_redo.commit_action()
	else:
		parent.add_child(duplicate)
		duplicate.owner = root
		_set_owner_recursive(duplicate, root)

	return {"result": {
		"original_path": str(node.get_path()),
		"duplicate_path": str(duplicate.get_path()),
		"duplicate_name": duplicate.name
	}}

func handle_get_groups(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var groups = node.get_groups()
	var group_list = []
	for group in groups:
		group_list.append(str(group))

	return {"result": {
		"node_path": str(node.get_path()),
		"groups": group_list,
		"count": group_list.size()
	}}

func handle_add_to_group(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var group_name = params.get("group_name", "")
	var persistent = params.get("persistent", false)

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if group_name.is_empty():
		return {"error": "Group name is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	if node.is_in_group(group_name):
		return {"result": {
			"node_path": str(node.get_path()),
			"group": group_name,
			"already_member": true
		}}

	# Add to group with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Add to Group: %s" % group_name)
		undo_redo.add_do_method(node, "add_to_group", group_name, persistent)
		undo_redo.add_undo_method(node, "remove_from_group", group_name)
		undo_redo.commit_action()
	else:
		node.add_to_group(group_name, persistent)

	return {"result": {
		"node_path": str(node.get_path()),
		"group": group_name,
		"persistent": persistent,
		"added": true
	}}

func handle_remove_from_group(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var group_name = params.get("group_name", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if group_name.is_empty():
		return {"error": "Group name is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	if not node.is_in_group(group_name):
		return {"result": {
			"node_path": str(node.get_path()),
			"group": group_name,
			"not_member": true
		}}

	# Remove from group with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Remove from Group: %s" % group_name)
		undo_redo.add_do_method(node, "remove_from_group", group_name)
		undo_redo.add_undo_method(node, "add_to_group", group_name)
		undo_redo.commit_action()
	else:
		node.remove_from_group(group_name)

	return {"result": {
		"node_path": str(node.get_path()),
		"group": group_name,
		"removed": true
	}}

func handle_find(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", ".")
	var pattern = params.get("pattern", "*")
	var type_filter = params.get("type", "")
	var recursive = params.get("recursive", true)
	var owned = params.get("owned", true)

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var found_nodes = node.find_children(pattern, type_filter, recursive, owned)
	var results = []

	for found in found_nodes:
		results.append({
			"name": found.name,
			"type": found.get_class(),
			"path": str(found.get_path())
		})

	return {"result": {
		"search_root": str(node.get_path()),
		"pattern": pattern,
		"type_filter": type_filter if not type_filter.is_empty() else null,
		"found": results,
		"count": results.size()
	}}

func handle_reorder(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var new_index = params.get("new_index", 0)

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	if node_path == "." or node_path == root.name:
		return {"error": "Cannot reorder the root node"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var parent = node.get_parent()
	if not parent:
		return {"error": "Node has no parent"}

	var old_index = node.get_index()
	var max_index = parent.get_child_count() - 1

	if new_index < 0 or new_index > max_index:
		return {"error": "Invalid index: %d (valid range: 0-%d)" % [new_index, max_index]}

	# Reorder with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Reorder Node: %s" % node.name)
		undo_redo.add_do_method(parent, "move_child", node, new_index)
		undo_redo.add_undo_method(parent, "move_child", node, old_index)
		undo_redo.commit_action()
	else:
		parent.move_child(node, new_index)

	return {"result": {
		"node_path": str(node.get_path()),
		"old_index": old_index,
		"new_index": node.get_index()
	}}

func handle_get_signal_connections(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var signal_filter = params.get("signal_name", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var connections = []
	var signal_list = node.get_signal_list()

	for sig in signal_list:
		var sig_name = sig["name"]
		if not signal_filter.is_empty() and sig_name != signal_filter:
			continue

		var conn_list = node.get_signal_connection_list(sig_name)
		for conn in conn_list:
			var target = conn.get("callable", Callable()).get_object()
			var method = conn.get("callable", Callable()).get_method()
			connections.append({
				"signal": sig_name,
				"target": str(target.get_path()) if target is Node else str(target),
				"method": str(method),
				"flags": conn.get("flags", 0)
			})

	return {"result": {
		"node_path": str(node.get_path()),
		"connections": connections,
		"count": connections.size()
	}}

func handle_disconnect_signal(params: Dictionary) -> Dictionary:
	var source_path = params.get("source_path", "")
	var signal_name = params.get("signal_name", "")
	var target_path = params.get("target_path", "")
	var method_name = params.get("method_name", "")

	if source_path.is_empty() or target_path.is_empty():
		return {"error": "Source and target paths are required"}

	if signal_name.is_empty() or method_name.is_empty():
		return {"error": "Signal name and method name are required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var source = _find_node_by_path(root, source_path)
	if not source:
		return {"error": "Source node not found: %s" % source_path}

	var target = _find_node_by_path(root, target_path)
	if not target:
		return {"error": "Target node not found: %s" % target_path}

	var callable = Callable(target, method_name)

	if not source.is_connected(signal_name, callable):
		return {"error": "Signal not connected: %s -> %s.%s" % [signal_name, target_path, method_name]}

	# Disconnect with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Disconnect Signal: %s" % signal_name)
		undo_redo.add_do_method(source, "disconnect", signal_name, callable)
		undo_redo.add_undo_method(source, "connect", signal_name, callable)
		undo_redo.commit_action()
	else:
		source.disconnect(signal_name, callable)

	return {"result": {
		"source": str(source.get_path()),
		"signal": signal_name,
		"target": str(target.get_path()),
		"method": method_name,
		"disconnected": true
	}}

func handle_set_owner(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var owner_path = params.get("owner_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if owner_path.is_empty():
		return {"error": "Owner path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	var new_owner = _find_node_by_path(root, owner_path)
	if not new_owner:
		return {"error": "Owner node not found: %s" % owner_path}

	var old_owner = node.owner

	# Set owner with undo/redo support
	var undo_redo = _get_undo_redo()
	if undo_redo:
		undo_redo.create_action("Set Owner: %s" % node.name)
		undo_redo.add_do_property(node, "owner", new_owner)
		undo_redo.add_undo_property(node, "owner", old_owner)
		undo_redo.commit_action()
	else:
		node.owner = new_owner

	return {"result": {
		"node_path": str(node.get_path()),
		"old_owner": str(old_owner.get_path()) if old_owner else null,
		"new_owner": str(new_owner.get_path()),
		"set": true
	}}
