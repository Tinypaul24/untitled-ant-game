@tool
extends Node

func _get_editor_filesystem() -> EditorFileSystem:
	return EditorInterface.get_resource_filesystem()

func _update_script_file(path: String) -> void:
	var filesystem = _get_editor_filesystem()
	if filesystem:
		filesystem.update_file(path)

func handle_list(params: Dictionary) -> Dictionary:
	var filter_path = params.get("path", "res://")

	if not filter_path.begins_with("res://"):
		filter_path = "res://" + filter_path

	if not DirAccess.dir_exists_absolute(filter_path):
		return {"error": "Failed to open directory: %s" % filter_path}

	var scripts = []
	_scan_scripts(filter_path.trim_prefix("res://"), scripts)

	return {"result": {"scripts": scripts}}

func _scan_scripts(base_path: String, scripts: Array) -> void:
	var full_path = "res://" + base_path

	# Scan files in current directory
	var files = DirAccess.get_files_at(full_path)
	for file_name in files:
		if file_name.begins_with("."):
			continue
		if file_name.ends_with(".gd"):
			scripts.append(full_path + file_name)

	# Recursively scan subdirectories
	var dirs = DirAccess.get_directories_at(full_path)
	for dir_name in dirs:
		if dir_name.begins_with("."):
			continue
		_scan_scripts(base_path + dir_name + "/", scripts)

func handle_read(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {"error": "Failed to open script: %s" % path}

	var content = file.get_as_text()
	file.close()

	var metadata = _parse_script_metadata(content)

	return {"result": {
		"path": path,
		"content": content,
		"class_name": metadata.class_name,
		"extends_class": metadata.extends_class,
		"functions": metadata.functions,
		"signals": metadata.signals,
		"properties": metadata.properties
	}}

func _parse_script_metadata(content: String) -> Dictionary:
	var result = {
		"class_name": "",
		"extends_class": "",
		"functions": [],
		"signals": [],
		"properties": []
	}

	var lines = content.split("\n")
	for line in lines:
		line = line.strip_edges()

		if line.begins_with("class_name "):
			result.class_name = line.substr(11).strip_edges()
		elif line.begins_with("extends "):
			result.extends_class = line.substr(8).strip_edges()
		elif line.begins_with("func "):
			var func_match = line.substr(5)
			var paren_pos = func_match.find("(")
			if paren_pos > 0:
				result.functions.append(func_match.substr(0, paren_pos))
		elif line.begins_with("signal "):
			var signal_match = line.substr(7)
			var paren_pos = signal_match.find("(")
			if paren_pos > 0:
				result.signals.append(signal_match.substr(0, paren_pos))
			else:
				result.signals.append(signal_match.strip_edges())
		elif line.begins_with("var ") or line.begins_with("@export var "):
			var var_line = line
			if line.begins_with("@export"):
				var_line = line.substr(line.find("var ") + 4)
			else:
				var_line = line.substr(4)

			var equals_pos = var_line.find("=")
			var colon_pos = var_line.find(":")
			var end_pos = var_line.length()

			if equals_pos > 0:
				end_pos = equals_pos
			if colon_pos > 0 and colon_pos < end_pos:
				end_pos = colon_pos

			var prop_name = var_line.substr(0, end_pos).strip_edges()
			if not prop_name.is_empty():
				result.properties.append(prop_name)

	return result

func handle_create(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var extends_class = params.get("extends_class", "Node")
	var class_name_param = params.get("class_name", "")
	var content = params.get("content", "")

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not path.ends_with(".gd"):
		path += ".gd"

	if FileAccess.file_exists(path):
		return {"error": "Script file already exists: %s" % path}

	# Create directory if needed
	var dir_path = path.get_base_dir()
	if not DirAccess.dir_exists_absolute(dir_path):
		var dir = DirAccess.open("res://")
		if not dir:
			return {"error": "Failed to access project directory"}
		var err = dir.make_dir_recursive(dir_path.trim_prefix("res://"))
		if err != OK:
			return {"error": "Failed to create directory '%s': error code %d" % [dir_path, err]}

	# Generate content if not provided
	if content.is_empty():
		content = "extends %s\n" % extends_class
		if not class_name_param.is_empty():
			content += "class_name %s\n" % class_name_param
		content += "\n\n"
		content += "func _ready() -> void:\n"
		content += "\tpass\n"

	var file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"error": "Failed to create script: %s" % path}

	file.store_string(content)
	file.close()
	_update_script_file(path)

	return {"result": {
		"path": path,
		"extends_class": extends_class,
		"class_name": class_name_param
	}}

func handle_modify(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var content = params.get("content", "")

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"error": "Failed to open script for writing: %s" % path}

	file.store_string(content)
	file.close()
	_update_script_file(path)

	return {"result": {"path": path, "success": true}}

func handle_get_metadata(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {"error": "Failed to open script: %s" % path}

	var content = file.get_as_text()
	file.close()

	var metadata = _parse_script_metadata(content)
	metadata["path"] = path
	metadata["line_count"] = content.split("\n").size()

	return {"result": metadata}

func handle_add_function(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var function_name = params.get("function_name", "")
	var parameters = params.get("parameters", "")
	var return_type = params.get("return_type", "void")
	var body = params.get("body", "pass")

	if path.is_empty():
		return {"error": "Script path is required"}

	if function_name.is_empty():
		return {"error": "Function name is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {"error": "Failed to open script: %s" % path}

	var content = file.get_as_text()
	file.close()

	# Build function definition
	var func_def = "\n\nfunc %s(%s)" % [function_name, parameters]
	if not return_type.is_empty() and return_type != "void":
		func_def += " -> %s" % return_type
	else:
		func_def += " -> void"
	func_def += ":\n"

	# Add body with indentation
	for line in body.split("\n"):
		func_def += "\t%s\n" % line

	content += func_def

	file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"error": "Failed to write to script: %s" % path}

	file.store_string(content)
	file.close()
	_update_script_file(path)

	return {"result": {
		"path": path,
		"function_name": function_name,
		"success": true
	}}

func handle_add_signal(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var signal_name = params.get("signal_name", "")
	var signal_parameters = params.get("parameters", [])

	if path.is_empty():
		return {"error": "Script path is required"}

	if signal_name.is_empty():
		return {"error": "Signal name is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {"error": "Failed to open script: %s" % path}

	var content = file.get_as_text()
	file.close()

	# Build signal definition
	var signal_def = "signal %s" % signal_name
	if signal_parameters.size() > 0:
		var param_strings = []
		for param in signal_parameters:
			if param is Dictionary:
				var param_name = param.get("name", "")
				var param_type = param.get("type", "")
				if not param_name.is_empty():
					if not param_type.is_empty():
						param_strings.append("%s: %s" % [param_name, param_type])
					else:
						param_strings.append(param_name)
			elif param is String:
				param_strings.append(param)
		signal_def += "(%s)" % ", ".join(param_strings)
	signal_def += "\n"

	# Find where to insert the signal (after extends/class_name, before first var/func)
	var lines = content.split("\n")
	var insert_index = 0
	var found_extends = false
	var found_class_name = false

	for i in range(lines.size()):
		var line = lines[i].strip_edges()
		if line.begins_with("extends "):
			found_extends = true
			insert_index = i + 1
		elif line.begins_with("class_name "):
			found_class_name = true
			insert_index = i + 1
		elif line.begins_with("signal "):
			insert_index = i + 1
		elif line.begins_with("var ") or line.begins_with("@export") or line.begins_with("func ") or line.begins_with("@onready"):
			break

	# Insert the signal
	lines.insert(insert_index, signal_def)
	content = "\n".join(lines)

	file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"error": "Failed to write to script: %s" % path}

	file.store_string(content)
	file.close()
	_update_script_file(path)

	return {"result": {
		"path": path,
		"signal_name": signal_name,
		"success": true
	}}

func handle_add_export(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var var_name = params.get("var_name", "")
	var var_type = params.get("type", "")
	var default_value = params.get("default_value", "")
	var hint = params.get("hint", "")

	if path.is_empty():
		return {"error": "Script path is required"}

	if var_name.is_empty():
		return {"error": "Variable name is required"}

	if var_type.is_empty():
		return {"error": "Variable type is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {"error": "Failed to open script: %s" % path}

	var content = file.get_as_text()
	file.close()

	# Build export variable definition
	var var_def = "@export"
	if not hint.is_empty():
		var_def += "_%s" % hint
	var_def += " var %s: %s" % [var_name, var_type]
	if not default_value.is_empty():
		var_def += " = %s" % default_value
	var_def += "\n"

	# Find where to insert (after signals, before functions)
	var lines = content.split("\n")
	var insert_index = 0
	var found_export_section = false

	for i in range(lines.size()):
		var line = lines[i].strip_edges()
		if line.begins_with("extends ") or line.begins_with("class_name "):
			insert_index = i + 1
		elif line.begins_with("signal "):
			insert_index = i + 1
		elif line.begins_with("@export"):
			insert_index = i + 1
			found_export_section = true
		elif line.begins_with("var ") or line.begins_with("@onready"):
			if not found_export_section:
				insert_index = i
				break
			insert_index = i + 1
		elif line.begins_with("func "):
			break

	# Insert the variable
	lines.insert(insert_index, var_def)
	content = "\n".join(lines)

	file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"error": "Failed to write to script: %s" % path}

	file.store_string(content)
	file.close()
	_update_script_file(path)

	return {"result": {
		"path": path,
		"var_name": var_name,
		"type": var_type,
		"success": true
	}}

func handle_get_current_script(_params: Dictionary) -> Dictionary:
	var script_editor = EditorInterface.get_script_editor()
	if not script_editor:
		return {"error": "Script editor not available"}

	var current_script = script_editor.get_current_script()
	if not current_script:
		return {"result": {"script_found": false}}

	var path = current_script.resource_path
	var content = ""

	# Read script content from file for the most up-to-date source
	if not path.is_empty():
		var file = FileAccess.open(path, FileAccess.READ)
		if file:
			content = file.get_as_text()
			file.close()
		else:
			push_warning("[Godot MCP] Could not read script file: %s" % path)

	var metadata = _parse_script_metadata(content)

	return {"result": {
		"script_found": true,
		"path": path,
		"content": content,
		"class_name": metadata.class_name,
		"extends_class": metadata.extends_class,
		"functions": metadata.functions,
		"signals": metadata.signals,
		"properties": metadata.properties
	}}

func handle_add_property(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var var_name = params.get("var_name", "")
	var var_type = params.get("type", "")
	var default_value = params.get("default_value", "")

	if path.is_empty():
		return {"error": "Script path is required"}

	if var_name.is_empty():
		return {"error": "Variable name is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var file = FileAccess.open(path, FileAccess.READ)
	if not file:
		return {"error": "Failed to open script: %s" % path}

	var content = file.get_as_text()
	file.close()

	# Build variable definition
	var var_def = "var %s" % var_name
	if not var_type.is_empty():
		var_def += ": %s" % var_type
	if not default_value.is_empty():
		var_def += " = %s" % default_value
	var_def += "\n"

	# Find where to insert (after exports, before functions)
	var lines = content.split("\n")
	var insert_index = 0

	for i in range(lines.size()):
		var line = lines[i].strip_edges()
		if line.begins_with("extends ") or line.begins_with("class_name "):
			insert_index = i + 1
		elif line.begins_with("signal "):
			insert_index = i + 1
		elif line.begins_with("@export") or line.begins_with("@onready"):
			insert_index = i + 1
		elif line.begins_with("var "):
			insert_index = i + 1
		elif line.begins_with("func "):
			break

	# Insert the variable
	lines.insert(insert_index, var_def)
	content = "\n".join(lines)

	file = FileAccess.open(path, FileAccess.WRITE)
	if not file:
		return {"error": "Failed to write to script: %s" % path}

	file.store_string(content)
	file.close()
	_update_script_file(path)

	return {"result": {
		"path": path,
		"var_name": var_name,
		"type": var_type if not var_type.is_empty() else "Variant",
		"success": true
	}}

func handle_get_open_scripts(_params: Dictionary) -> Dictionary:
	var script_editor = EditorInterface.get_script_editor()
	if not script_editor:
		return {"error": "Script editor not available"}

	var open_scripts = script_editor.get_open_scripts()
	var result = []

	for script in open_scripts:
		if script:
			result.append({
				"path": script.resource_path,
				"type": script.get_class()
			})

	return {"result": {
		"open_scripts": result,
		"count": result.size()
	}}

func handle_goto_line(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var line = params.get("line", 1)
	var column = params.get("column", 1)

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var script_editor = EditorInterface.get_script_editor()
	if not script_editor:
		return {"error": "Script editor not available"}

	# Load and open the script
	var script = load(path)
	if not script:
		return {"error": "Failed to load script: %s" % path}

	# Open the script in the editor
	EditorInterface.edit_script(script, line - 1, column - 1)

	return {"result": {
		"path": path,
		"line": line,
		"column": column,
		"success": true
	}}

func handle_get_breakpoints(params: Dictionary) -> Dictionary:
	var filter_path = params.get("path", "")

	var script_editor = EditorInterface.get_script_editor()
	if not script_editor:
		return {"error": "Script editor not available"}

	var breakpoints = script_editor.get_breakpoints()
	var result = []

	for bp in breakpoints:
		# bp format: "res://path/to/script.gd:line_number"
		if bp is String:
			var parts = bp.rsplit(":", false, 1)
			if parts.size() == 2:
				var bp_path = parts[0]
				var bp_line = parts[1].to_int()

				if filter_path.is_empty() or bp_path == filter_path:
					result.append({
						"path": bp_path,
						"line": bp_line
					})

	return {"result": {
		"breakpoints": result,
		"count": result.size()
	}}

func handle_get_methods(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var include_inherited = params.get("include_inherited", false)

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var script = load(path)
	if not script:
		return {"error": "Failed to load script: %s" % path}

	var methods = script.get_script_method_list()
	var result = []

	for method in methods:
		var method_info = {
			"name": method["name"],
			"args": [],
			"return_type": _get_type_name(method.get("return", {}).get("type", TYPE_NIL))
		}

		for arg in method.get("args", []):
			method_info["args"].append({
				"name": arg.get("name", ""),
				"type": _get_type_name(arg.get("type", TYPE_NIL))
			})

		# Filter inherited methods if needed
		if not include_inherited:
			# Methods declared in this script typically don't have PROPERTY_USAGE_SCRIPT_VARIABLE
			# This is a heuristic - not perfect but works for most cases
			if method.get("flags", 0) & METHOD_FLAG_VIRTUAL == 0:
				result.append(method_info)
		else:
			result.append(method_info)

	return {"result": {
		"path": path,
		"methods": result,
		"count": result.size()
	}}

func handle_get_signals(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	var script = load(path)
	if not script:
		return {"error": "Failed to load script: %s" % path}

	var signals = script.get_script_signal_list()
	var result = []

	for sig in signals:
		var signal_info = {
			"name": sig["name"],
			"args": []
		}

		for arg in sig.get("args", []):
			signal_info["args"].append({
				"name": arg.get("name", ""),
				"type": _get_type_name(arg.get("type", TYPE_NIL))
			})

		result.append(signal_info)

	return {"result": {
		"path": path,
		"signals": result,
		"count": result.size()
	}}

func _get_type_name(type_id: int) -> String:
	match type_id:
		TYPE_NIL: return "Nil"
		TYPE_BOOL: return "bool"
		TYPE_INT: return "int"
		TYPE_FLOAT: return "float"
		TYPE_STRING: return "String"
		TYPE_VECTOR2: return "Vector2"
		TYPE_VECTOR2I: return "Vector2i"
		TYPE_RECT2: return "Rect2"
		TYPE_RECT2I: return "Rect2i"
		TYPE_VECTOR3: return "Vector3"
		TYPE_VECTOR3I: return "Vector3i"
		TYPE_TRANSFORM2D: return "Transform2D"
		TYPE_VECTOR4: return "Vector4"
		TYPE_VECTOR4I: return "Vector4i"
		TYPE_PLANE: return "Plane"
		TYPE_QUATERNION: return "Quaternion"
		TYPE_AABB: return "AABB"
		TYPE_BASIS: return "Basis"
		TYPE_TRANSFORM3D: return "Transform3D"
		TYPE_PROJECTION: return "Projection"
		TYPE_COLOR: return "Color"
		TYPE_STRING_NAME: return "StringName"
		TYPE_NODE_PATH: return "NodePath"
		TYPE_RID: return "RID"
		TYPE_OBJECT: return "Object"
		TYPE_CALLABLE: return "Callable"
		TYPE_SIGNAL: return "Signal"
		TYPE_DICTIONARY: return "Dictionary"
		TYPE_ARRAY: return "Array"
		TYPE_PACKED_BYTE_ARRAY: return "PackedByteArray"
		TYPE_PACKED_INT32_ARRAY: return "PackedInt32Array"
		TYPE_PACKED_INT64_ARRAY: return "PackedInt64Array"
		TYPE_PACKED_FLOAT32_ARRAY: return "PackedFloat32Array"
		TYPE_PACKED_FLOAT64_ARRAY: return "PackedFloat64Array"
		TYPE_PACKED_STRING_ARRAY: return "PackedStringArray"
		TYPE_PACKED_VECTOR2_ARRAY: return "PackedVector2Array"
		TYPE_PACKED_VECTOR3_ARRAY: return "PackedVector3Array"
		TYPE_PACKED_COLOR_ARRAY: return "PackedColorArray"
		TYPE_PACKED_VECTOR4_ARRAY: return "PackedVector4Array"
		_: return "Unknown (type_id: %d)" % type_id
