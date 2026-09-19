@tool
extends Node

func _get_editor_filesystem() -> EditorFileSystem:
	return EditorInterface.get_resource_filesystem()

func handle_scan(_params: Dictionary) -> Dictionary:
	var filesystem = _get_editor_filesystem()
	if not filesystem:
		return {"error": "Could not access EditorFileSystem"}

	filesystem.scan()

	return {"result": {
		"initiated": true,
		"message": "Filesystem scan initiated",
		"note": "Scan runs asynchronously. Check editor for completion status."
	}}

func handle_reimport(params: Dictionary) -> Dictionary:
	var paths = params.get("paths", [])

	if paths.is_empty():
		return {"error": "At least one path is required"}

	var filesystem = _get_editor_filesystem()
	if not filesystem:
		return {"error": "Could not access EditorFileSystem"}

	# Validate paths exist
	var valid_paths = []
	var invalid_paths = []
	for path in paths:
		if not path.begins_with("res://"):
			path = "res://" + path
		if FileAccess.file_exists(path):
			valid_paths.append(path)
		else:
			invalid_paths.append(path)

	if valid_paths.is_empty():
		return {"error": "None of the specified files exist: %s" % str(invalid_paths)}

	filesystem.reimport_files(valid_paths)

	var result = {
		"success": true,
		"reimported": valid_paths,
		"count": valid_paths.size()
	}

	if not invalid_paths.is_empty():
		result["not_found"] = invalid_paths

	return {"result": result}

func handle_get_file_type(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "File path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	var filesystem = _get_editor_filesystem()
	if not filesystem:
		return {"error": "Could not access EditorFileSystem"}

	if not FileAccess.file_exists(path):
		return {"error": "File not found: %s" % path}

	var file_type = filesystem.get_file_type(path)

	return {"result": {
		"path": path,
		"type": file_type
	}}

func handle_update_file(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "File path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	var filesystem = _get_editor_filesystem()
	if not filesystem:
		return {"error": "Could not access EditorFileSystem"}

	if not FileAccess.file_exists(path):
		return {"error": "File not found: %s" % path}

	filesystem.update_file(path)

	return {"result": {
		"success": true,
		"path": path,
		"message": "File update notification sent"
	}}

func handle_create_directory(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "Directory path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	# Protect critical directories
	if path == "res://.godot" or path.begins_with("res://.godot/"):
		return {"error": "Cannot create directories inside .godot/"}

	if DirAccess.dir_exists_absolute(path):
		return {"result": {
			"path": path,
			"already_exists": true,
			"created": false
		}}

	var err = DirAccess.make_dir_recursive_absolute(path)
	if err != OK:
		return {"error": "Failed to create directory: %s (error: %d)" % [path, err]}

	# Refresh editor filesystem
	var filesystem = _get_editor_filesystem()
	if filesystem:
		filesystem.scan()

	return {"result": {
		"path": path,
		"created": true
	}}

func handle_delete_file(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var recursive = params.get("recursive", false)

	if path.is_empty():
		return {"error": "File path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	# Protect critical files/directories
	var protected_paths = ["res://.godot", "res://project.godot", "res://addons/godot_mcp"]
	for protected_path in protected_paths:
		if path == protected_path or path.begins_with(protected_path + "/"):
			return {"error": "Cannot delete protected path: %s" % path}

	var is_dir = DirAccess.dir_exists_absolute(path)
	var is_file = FileAccess.file_exists(path)

	if not is_dir and not is_file:
		return {"error": "File or directory not found: %s" % path}

	var err: int
	if is_dir and recursive:
		err = _delete_directory_recursive(path)
	elif is_dir:
		err = DirAccess.remove_absolute(path)
	else:
		err = DirAccess.remove_absolute(path)

	if err != OK:
		return {"error": "Failed to delete: %s (error: %d)" % [path, err]}

	if not is_dir:
		_handle_uid_companion("delete", path)

	# Refresh editor filesystem
	var filesystem = _get_editor_filesystem()
	if filesystem:
		filesystem.scan()

	return {"result": {
		"path": path,
		"deleted": true,
		"was_directory": is_dir,
		"recursive": recursive
	}}

func handle_rename_file(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var new_name = params.get("new_name", "")

	if path.is_empty():
		return {"error": "File path is required"}

	if new_name.is_empty():
		return {"error": "New name is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	# Protect critical files/directories
	var protected_paths = ["res://.godot", "res://project.godot", "res://addons/godot_mcp"]
	for protected_path in protected_paths:
		if path == protected_path or path.begins_with(protected_path + "/"):
			return {"error": "Cannot rename protected path: %s" % path}

	if not FileAccess.file_exists(path) and not DirAccess.dir_exists_absolute(path):
		return {"error": "File or directory not found: %s" % path}

	# Construct new path by replacing the file name
	var dir = path.get_base_dir()
	var new_path = dir + "/" + new_name

	if FileAccess.file_exists(new_path) or DirAccess.dir_exists_absolute(new_path):
		return {"error": "Destination already exists: %s" % new_path}

	var err = DirAccess.rename_absolute(path, new_path)
	if err != OK:
		return {"error": "Failed to rename: %s → %s (error: %d)" % [path, new_path, err]}

	_handle_uid_companion("rename", path, new_path)

	# Refresh editor filesystem
	var filesystem = _get_editor_filesystem()
	if filesystem:
		filesystem.scan()

	return {"result": {
		"old_path": path,
		"new_path": new_path,
		"renamed": true
	}}

func handle_move_file(params: Dictionary) -> Dictionary:
	var source = params.get("source", "")
	var destination = params.get("destination", "")

	if source.is_empty():
		return {"error": "Source path is required"}

	if destination.is_empty():
		return {"error": "Destination path is required"}

	if not source.begins_with("res://"):
		source = "res://" + source

	if not destination.begins_with("res://"):
		destination = "res://" + destination

	# Protect critical files/directories
	var protected_paths = ["res://.godot", "res://project.godot", "res://addons/godot_mcp"]
	for protected_path in protected_paths:
		if source == protected_path or source.begins_with(protected_path + "/"):
			return {"error": "Cannot move protected path: %s" % source}

	if not FileAccess.file_exists(source) and not DirAccess.dir_exists_absolute(source):
		return {"error": "Source not found: %s" % source}

	if FileAccess.file_exists(destination) or DirAccess.dir_exists_absolute(destination):
		return {"error": "Destination already exists: %s" % destination}

	# Ensure destination directory exists
	var dest_dir = destination.get_base_dir()
	if not DirAccess.dir_exists_absolute(dest_dir):
		var mkdir_err = DirAccess.make_dir_recursive_absolute(dest_dir)
		if mkdir_err != OK:
			return {"error": "Failed to create destination directory: %s (error: %d)" % [dest_dir, mkdir_err]}

	var err = DirAccess.rename_absolute(source, destination)
	if err != OK:
		return {"error": "Failed to move: %s → %s (error: %d)" % [source, destination, err]}

	_handle_uid_companion("move", source, destination)

	# Refresh editor filesystem
	var filesystem = _get_editor_filesystem()
	if filesystem:
		filesystem.scan()

	return {"result": {
		"source": source,
		"destination": destination,
		"moved": true
	}}

# --- Helper functions ---

func _handle_uid_companion(operation: String, path: String, dest_path: String = "") -> void:
	var uid_path = path + ".uid"
	if not FileAccess.file_exists(uid_path):
		return
	match operation:
		"delete":
			var err = DirAccess.remove_absolute(uid_path)
			if err != OK:
				push_warning("[Godot MCP] Failed to delete UID companion: %s (error: %d)" % [uid_path, err])
		"rename", "move":
			if not dest_path.is_empty():
				var dest_uid = dest_path + ".uid"
				var err = DirAccess.rename_absolute(uid_path, dest_uid)
				if err != OK:
					push_warning("[Godot MCP] Failed to move UID companion: %s (error: %d)" % [uid_path, err])

func _delete_directory_recursive(path: String) -> int:
	var dir = DirAccess.open(path)
	if dir == null:
		return ERR_CANT_OPEN
	dir.list_dir_begin()
	var file_name = dir.get_next()
	while file_name != "":
		if file_name == "." or file_name == "..":
			file_name = dir.get_next()
			continue
		var full_path = path + "/" + file_name
		if dir.current_is_dir():
			var sub_err = _delete_directory_recursive(full_path)
			if sub_err != OK:
				dir.list_dir_end()
				return sub_err
		else:
			var file_err = DirAccess.remove_absolute(full_path)
			if file_err != OK:
				dir.list_dir_end()
				return file_err
		file_name = dir.get_next()
	dir.list_dir_end()
	return DirAccess.remove_absolute(path)
