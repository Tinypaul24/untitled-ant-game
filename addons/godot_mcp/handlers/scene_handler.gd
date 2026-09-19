@tool
extends Node

func handle_list(params: Dictionary) -> Dictionary:
	var filter_path = params.get("path", "res://")

	if not filter_path.begins_with("res://"):
		filter_path = "res://" + filter_path

	if not DirAccess.dir_exists_absolute(filter_path):
		return {"error": "Failed to open directory: %s" % filter_path}

	var scenes = []
	_scan_scenes(filter_path.trim_prefix("res://"), scenes)

	return {"result": {"scenes": scenes}}

func _scan_scenes(base_path: String, scenes: Array) -> void:
	var full_path = "res://" + base_path

	# Scan files in current directory
	var files = DirAccess.get_files_at(full_path)
	for file_name in files:
		if file_name.begins_with("."):
			continue
		if file_name.ends_with(".tscn") or file_name.ends_with(".scn"):
			scenes.append(full_path + file_name)

	# Recursively scan subdirectories
	var dirs = DirAccess.get_directories_at(full_path)
	for dir_name in dirs:
		if dir_name.begins_with("."):
			continue
		_scan_scenes(base_path + dir_name + "/", scenes)

func handle_read(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var include_properties = params.get("include_properties", false)

	if path.is_empty():
		return {"error": "Scene path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Scene file not found: %s" % path}

	var packed_scene = load(path)
	if not packed_scene:
		return {"error": "Failed to load scene: %s" % path}

	var scene_instance = packed_scene.instantiate()
	if not scene_instance:
		return {"error": "Failed to instantiate scene: %s" % path}

	var structure = _get_node_structure(scene_instance, include_properties)
	scene_instance.queue_free()

	return {"result": {
		"path": path,
		"root_node": structure
	}}

func _get_node_structure(node: Node, include_properties: bool) -> Dictionary:
	var structure = {
		"name": node.name,
		"type": node.get_class(),
		"path": str(node.get_path())
	}

	var script = node.get_script()
	if script:
		structure["script"] = script.resource_path

	if include_properties:
		var properties = {}
		for prop in node.get_property_list():
			var prop_name = prop["name"]
			if prop_name.begins_with("_") or prop_name in ["script"]:
				continue
			if prop["usage"] & PROPERTY_USAGE_EDITOR:
				var value = node.get(prop_name)
				if value != null:
					properties[prop_name] = _serialize_value(value)
		structure["properties"] = properties

	var children = []
	for child in node.get_children():
		children.append(_get_node_structure(child, include_properties))

	if children.size() > 0:
		structure["children"] = children

	return structure

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

func handle_create(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var root_type = params.get("root_type", "Node2D")
	var root_name = params.get("root_name", "")

	if path.is_empty():
		return {"error": "Scene path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not path.ends_with(".tscn"):
		path += ".tscn"

	if FileAccess.file_exists(path):
		return {"error": "Scene file already exists: %s" % path}

	# Create directory if needed
	var dir_path = path.get_base_dir()
	if not DirAccess.dir_exists_absolute(dir_path):
		var dir = DirAccess.open("res://")
		if dir:
			var err = dir.make_dir_recursive(dir_path.trim_prefix("res://"))
			if err != OK:
				push_warning("[Godot MCP] Failed to create directory '%s': error code %d" % [dir_path, err])

	# Create root node
	var root_node: Node = null
	if ClassDB.class_exists(root_type):
		root_node = ClassDB.instantiate(root_type)
	else:
		return {"error": "Invalid node type: %s" % root_type}

	if root_name.is_empty():
		root_name = path.get_file().get_basename()
	root_node.name = root_name

	# Save scene
	var packed_scene = PackedScene.new()
	var result = packed_scene.pack(root_node)
	if result != OK:
		root_node.free()
		return {"error": "Failed to pack scene: %d" % result}

	result = ResourceSaver.save(packed_scene, path)
	if result != OK:
		root_node.free()
		return {"error": "Failed to save scene: %d" % result}

	root_node.free()

	return {"result": {
		"path": path,
		"root_type": root_type,
		"root_name": root_name
	}}

func handle_save(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	var plugin = Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null
	if not plugin:
		return {"error": "GodotMCPPlugin not found"}

	var editor_interface = plugin.get_editor_interface()
	var edited_scene_root = editor_interface.get_edited_scene_root()

	if not edited_scene_root:
		return {"error": "No scene is currently being edited"}

	if path.is_empty():
		path = edited_scene_root.scene_file_path

	if path.is_empty():
		return {"error": "Scene path is required for unsaved scenes"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not path.ends_with(".tscn"):
		path += ".tscn"

	var packed_scene = PackedScene.new()
	var result = packed_scene.pack(edited_scene_root)
	if result != OK:
		return {"error": "Failed to pack scene: %d" % result}

	result = ResourceSaver.save(packed_scene, path)
	if result != OK:
		return {"error": "Failed to save scene: %d" % result}

	return {"result": {"path": path}}

func handle_open(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "Scene path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Scene file not found: %s" % path}

	var plugin = Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null
	if not plugin:
		return {"error": "GodotMCPPlugin not found"}

	var editor_interface = plugin.get_editor_interface()
	editor_interface.open_scene_from_path(path)

	return {"result": {"path": path}}

func handle_get_current(_params: Dictionary) -> Dictionary:
	var plugin = Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null
	if not plugin:
		return {"error": "GodotMCPPlugin not found"}

	var editor_interface = plugin.get_editor_interface()
	var edited_scene_root = editor_interface.get_edited_scene_root()

	if not edited_scene_root:
		return {"result": {
			"path": "",
			"root_type": "",
			"root_name": "",
			"is_saved": false
		}}

	var scene_path = edited_scene_root.scene_file_path

	return {"result": {
		"path": scene_path if not scene_path.is_empty() else "Untitled",
		"root_type": edited_scene_root.get_class(),
		"root_name": edited_scene_root.name,
		"is_saved": not scene_path.is_empty()
	}}
