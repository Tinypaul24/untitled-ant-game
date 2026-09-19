@tool
extends Node

func handle_get_info(_params: Dictionary) -> Dictionary:
	var project_name = ProjectSettings.get_setting("application/config/name", "Untitled Project")
	var project_version = ProjectSettings.get_setting("application/config/version", "1.0.0")
	var project_path = ProjectSettings.globalize_path("res://")

	var version_info = Engine.get_version_info()
	var godot_version = "%s.%s.%s" % [
		version_info.get("major", 0),
		version_info.get("minor", 0),
		version_info.get("patch", 0)
	]

	var current_scene = ""
	var edited_scene_root = EditorInterface.get_edited_scene_root()
	if edited_scene_root:
		current_scene = edited_scene_root.scene_file_path

	return {"result": {
		"project_name": project_name,
		"project_version": project_version,
		"project_path": project_path,
		"godot_version": godot_version,
		"current_scene": current_scene
	}}

func handle_list_resources(params: Dictionary) -> Dictionary:
	var filter_type = params.get("type", "")
	var filter_path = params.get("path", "res://")

	if not filter_path.begins_with("res://"):
		filter_path = "res://" + filter_path

	if not DirAccess.dir_exists_absolute(filter_path):
		return {"error": "Failed to open directory: %s" % filter_path}

	var resources = {
		"scenes": [],
		"scripts": [],
		"textures": [],
		"audio": [],
		"resources": [],
		"other": []
	}

	_scan_resources(filter_path.trim_prefix("res://"), resources, filter_type)

	return {"result": resources}

func _scan_resources(base_path: String, resources: Dictionary, filter_type: String) -> void:
	var full_path = "res://" + base_path

	# Scan files in current directory
	var files = DirAccess.get_files_at(full_path)
	for file_name in files:
		if file_name.begins_with("."):
			continue

		var file_path = full_path + file_name
		var ext = file_name.get_extension().to_lower()

		if not filter_type.is_empty() and ext != filter_type:
			continue

		# Categorize by extension
		if ext == "tscn" or ext == "scn":
			resources["scenes"].append(file_path)
		elif ext == "gd" or ext == "cs":
			resources["scripts"].append(file_path)
		elif ext in ["png", "jpg", "jpeg", "webp", "svg"]:
			resources["textures"].append(file_path)
		elif ext in ["wav", "ogg", "mp3"]:
			resources["audio"].append(file_path)
		elif ext in ["tres", "res"]:
			resources["resources"].append(file_path)
		else:
			resources["other"].append(file_path)

	# Recursively scan subdirectories
	var dirs = DirAccess.get_directories_at(full_path)
	for dir_name in dirs:
		if dir_name.begins_with("."):
			continue
		_scan_resources(base_path + dir_name + "/", resources, filter_type)

func handle_get_settings(params: Dictionary) -> Dictionary:
	var category = params.get("category", "")

	var settings = {}

	if category.is_empty() or category == "application":
		settings["application"] = {
			"name": ProjectSettings.get_setting("application/config/name", ""),
			"version": ProjectSettings.get_setting("application/config/version", ""),
			"main_scene": ProjectSettings.get_setting("application/run/main_scene", "")
		}

	if category.is_empty() or category == "display":
		settings["display"] = {
			"width": ProjectSettings.get_setting("display/window/size/viewport_width", 1024),
			"height": ProjectSettings.get_setting("display/window/size/viewport_height", 600),
			"mode": ProjectSettings.get_setting("display/window/size/mode", 0),
			"resizable": ProjectSettings.get_setting("display/window/size/resizable", true)
		}

	if category.is_empty() or category == "physics":
		settings["physics"] = {
			"2d_gravity": ProjectSettings.get_setting("physics/2d/default_gravity", 980),
			"3d_gravity": ProjectSettings.get_setting("physics/3d/default_gravity", 9.8)
		}

	if category.is_empty() or category == "rendering":
		settings["rendering"] = {
			"renderer": ProjectSettings.get_setting("rendering/renderer/rendering_method", ""),
			"msaa_2d": ProjectSettings.get_setting("rendering/anti_aliasing/quality/msaa_2d", 0),
			"msaa_3d": ProjectSettings.get_setting("rendering/anti_aliasing/quality/msaa_3d", 0)
		}

	return {"result": settings}

func _get_edited_scene_root() -> Node:
	return EditorInterface.get_edited_scene_root()

func _find_node_by_path(root: Node, path: String) -> Node:
	if path == "." or path == root.name:
		return root
	if path.begins_with("./"):
		path = path.substr(2)
	return root.get_node_or_null(path)

func handle_save_all_scenes(_params: Dictionary) -> Dictionary:
	EditorInterface.save_all_scenes()

	return {"result": {
		"success": true,
		"message": "All scenes saved"
	}}

func handle_select_node_in_editor(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	EditorInterface.edit_node(node)

	var selection = EditorInterface.get_selection()
	if selection:
		selection.clear()
		selection.add_node(node)

	return {"result": {
		"selected": str(node.get_path()),
		"node_name": node.name,
		"node_type": node.get_class()
	}}

func handle_reload_scene_from_disk(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "Scene path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Scene file not found: %s" % path}

	# Workaround for godotengine/godot#114710: clear selection before reload
	# to avoid use-after-free crash in EditorData::get_handling_main_editor()
	EditorInterface.get_selection().clear()
	EditorInterface.reload_scene_from_path(path)

	return {"result": {
		"path": path,
		"reloaded": true
	}}

func handle_get_editor_setting(params: Dictionary) -> Dictionary:
	var setting = params.get("setting", "")

	if setting.is_empty():
		return {"error": "Setting path is required"}

	var settings = EditorInterface.get_editor_settings()
	if not settings:
		return {"error": "Could not access editor settings"}

	var value = settings.get_setting(setting)

	return {"result": {
		"setting": setting,
		"value": value,
		"has_setting": settings.has_setting(setting)
	}}

func handle_set_editor_setting(params: Dictionary) -> Dictionary:
	var setting = params.get("setting", "")
	var value = params.get("value", null)

	if setting.is_empty():
		return {"error": "Setting path is required"}

	var settings = EditorInterface.get_editor_settings()
	if not settings:
		return {"error": "Could not access editor settings"}

	settings.set_setting(setting, value)
	settings.mark_setting_changed(setting)

	return {"result": {
		"setting": setting,
		"value": value,
		"success": true
	}}

func handle_get_input_actions(_params: Dictionary) -> Dictionary:
	var actions = InputMap.get_actions()
	var result = []

	for action in actions:
		# Skip built-in UI actions unless they've been modified
		if str(action).begins_with("ui_"):
			continue

		var events = InputMap.action_get_events(action)
		var event_list = []
		for event in events:
			if event is InputEventKey:
				event_list.append({
					"type": "key",
					"keycode": OS.get_keycode_string(event.keycode)
				})
			elif event is InputEventMouseButton:
				event_list.append({
					"type": "mouse_button",
					"button_index": event.button_index
				})
			elif event is InputEventJoypadButton:
				event_list.append({
					"type": "joypad_button",
					"button_index": event.button_index
				})
			elif event is InputEventJoypadMotion:
				event_list.append({
					"type": "joypad_motion",
					"axis": event.axis,
					"axis_value": event.axis_value
				})

		result.append({
			"name": str(action),
			"deadzone": InputMap.action_get_deadzone(action),
			"events": event_list
		})

	return {"result": {
		"actions": result,
		"count": result.size()
	}}

func handle_add_input_action(params: Dictionary) -> Dictionary:
	var action_name = params.get("action_name", "")
	var deadzone = params.get("deadzone", 0.5)

	if action_name.is_empty():
		return {"error": "Action name is required"}

	if InputMap.has_action(action_name):
		return {"error": "Action already exists: %s" % action_name}

	InputMap.add_action(action_name, deadzone)

	# Also add to project settings so it persists
	var action_setting = "input/%s" % action_name
	ProjectSettings.set_setting(action_setting, {
		"deadzone": deadzone,
		"events": []
	})
	ProjectSettings.save()

	return {"result": {
		"action_name": action_name,
		"deadzone": deadzone,
		"created": true
	}}

# === EditorInterface tools ===

func handle_set_main_screen_editor(params: Dictionary) -> Dictionary:
	var screen_name = params.get("screen_name", "")

	if screen_name.is_empty():
		return {"error": "Screen name is required"}

	var valid_screens = ["2D", "3D", "Script", "AssetLib"]
	if screen_name not in valid_screens:
		return {"error": "Invalid screen name: %s. Valid: %s" % [screen_name, ", ".join(valid_screens)]}

	EditorInterface.set_main_screen_editor(screen_name)

	return {"result": {
		"screen_name": screen_name,
		"switched": true
	}}

func handle_get_open_scenes(_params: Dictionary) -> Dictionary:
	var scenes = EditorInterface.get_open_scenes()

	return {"result": {
		"scenes": scenes,
		"count": scenes.size()
	}}

func handle_inspect_object(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var property = params.get("property", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	if property.is_empty():
		EditorInterface.inspect_object(node)
	else:
		EditorInterface.inspect_object(node, property)

	return {"result": {
		"node_path": str(node.get_path()),
		"node_type": node.get_class(),
		"property": property if not property.is_empty() else null,
		"inspecting": true
	}}

func handle_edit_resource(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "Resource path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Resource file not found: %s" % path}

	var resource = load(path)
	if not resource:
		return {"error": "Failed to load resource: %s" % path}

	EditorInterface.edit_resource(resource)

	return {"result": {
		"path": path,
		"type": resource.get_class(),
		"editing": true
	}}

func handle_edit_script(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var line = params.get("line", -1)
	var column = params.get("column", 0)

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var script = load(path) as Script
	if not script:
		return {"error": "Failed to load script: %s" % path}

	EditorInterface.edit_script(script, line, column)
	EditorInterface.set_main_screen_editor("Script")

	return {"result": {
		"path": path,
		"line": line,
		"column": column,
		"opened": true
	}}

func handle_mark_scene_as_unsaved(_params: Dictionary) -> Dictionary:
	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	EditorInterface.mark_scene_as_unsaved()

	return {"result": {
		"scene": root.scene_file_path,
		"marked": true
	}}

# === Project settings tools ===

func handle_get_project_setting(params: Dictionary) -> Dictionary:
	var setting = params.get("setting", "")

	if setting.is_empty():
		return {"error": "Setting path is required"}

	if not ProjectSettings.has_setting(setting):
		return {"result": {
			"setting": setting,
			"exists": false,
			"value": null
		}}

	var value = ProjectSettings.get_setting(setting)

	return {"result": {
		"setting": setting,
		"exists": true,
		"value": value
	}}

func handle_set_project_setting(params: Dictionary) -> Dictionary:
	var setting = params.get("setting", "")
	var value = params.get("value", null)

	if setting.is_empty():
		return {"error": "Setting path is required"}

	var old_value = ProjectSettings.get_setting(setting) if ProjectSettings.has_setting(setting) else null
	ProjectSettings.set_setting(setting, value)
	ProjectSettings.save()

	return {"result": {
		"setting": setting,
		"old_value": old_value,
		"new_value": value,
		"saved": true
	}}

func handle_remove_input_action(params: Dictionary) -> Dictionary:
	var action_name = params.get("action_name", "")

	if action_name.is_empty():
		return {"error": "Action name is required"}

	if not InputMap.has_action(action_name):
		return {"error": "Action not found: %s" % action_name}

	InputMap.erase_action(action_name)

	# Remove from project settings
	var action_setting = "input/%s" % action_name
	if ProjectSettings.has_setting(action_setting):
		ProjectSettings.set_setting(action_setting, null)
		ProjectSettings.save()

	return {"result": {
		"action_name": action_name,
		"removed": true
	}}

func handle_add_input_event(params: Dictionary) -> Dictionary:
	var action_name = params.get("action_name", "")
	var event_type = params.get("event_type", "")

	if action_name.is_empty():
		return {"error": "Action name is required"}

	if event_type.is_empty():
		return {"error": "Event type is required"}

	if not InputMap.has_action(action_name):
		return {"error": "Action not found: %s" % action_name}

	var event: InputEvent = null

	match event_type:
		"key":
			var keycode_str = params.get("keycode", "")
			if keycode_str.is_empty():
				return {"error": "Keycode is required for key events"}
			var key_event = InputEventKey.new()
			key_event.keycode = OS.find_keycode_from_string(keycode_str)
			if key_event.keycode == KEY_NONE:
				return {"error": "Invalid keycode: %s" % keycode_str}
			event = key_event
		"mouse_button":
			var button_index = params.get("button_index", -1)
			if button_index < 0:
				return {"error": "Button index is required for mouse button events"}
			var mouse_event = InputEventMouseButton.new()
			mouse_event.button_index = button_index
			event = mouse_event
		"joypad_button":
			var button_index = params.get("button_index", -1)
			if button_index < 0:
				return {"error": "Button index is required for joypad button events"}
			var joy_event = InputEventJoypadButton.new()
			joy_event.button_index = button_index
			event = joy_event
		"joypad_motion":
			var axis = params.get("axis", -1)
			var axis_value = params.get("axis_value", 0.0)
			if axis < 0:
				return {"error": "Axis is required for joypad motion events"}
			var motion_event = InputEventJoypadMotion.new()
			motion_event.axis = axis
			motion_event.axis_value = axis_value
			event = motion_event
		_:
			return {"error": "Invalid event type: %s. Valid: key, mouse_button, joypad_button, joypad_motion" % event_type}

	InputMap.action_add_event(action_name, event)

	# Update project settings
	_save_input_action_to_settings(action_name)

	return {"result": {
		"action_name": action_name,
		"event_type": event_type,
		"added": true,
		"total_events": InputMap.action_get_events(action_name).size()
	}}

func handle_remove_input_event(params: Dictionary) -> Dictionary:
	var action_name = params.get("action_name", "")
	var event_index = params.get("event_index", -1)

	if action_name.is_empty():
		return {"error": "Action name is required"}

	if event_index < 0:
		return {"error": "Event index is required"}

	if not InputMap.has_action(action_name):
		return {"error": "Action not found: %s" % action_name}

	var events = InputMap.action_get_events(action_name)
	if event_index >= events.size():
		return {"error": "Event index out of range: %d (event count: %d)" % [event_index, events.size()]}

	var event = events[event_index]
	InputMap.action_erase_event(action_name, event)

	# Update project settings
	_save_input_action_to_settings(action_name)

	return {"result": {
		"action_name": action_name,
		"removed_index": event_index,
		"remaining_events": InputMap.action_get_events(action_name).size()
	}}

func _save_input_action_to_settings(action_name: String) -> void:
	var events = InputMap.action_get_events(action_name)
	var deadzone = InputMap.action_get_deadzone(action_name)
	var event_data = []
	for event in events:
		event_data.append(event)

	var action_setting = "input/%s" % action_name
	ProjectSettings.set_setting(action_setting, {
		"deadzone": deadzone,
		"events": event_data
	})
	ProjectSettings.save()
