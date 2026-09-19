@tool
extends Node

func _get_undo_redo() -> EditorUndoRedoManager:
	var plugin = Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null
	if not plugin:
		return null
	return plugin.get_undo_redo()

func _get_edited_scene_root() -> Node:
	var plugin = Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null
	if not plugin:
		return null
	var editor_interface = plugin.get_editor_interface()
	return editor_interface.get_edited_scene_root()

func handle_undo(_params: Dictionary) -> Dictionary:
	var undo_redo = _get_undo_redo()
	if not undo_redo:
		return {"error": "Could not access UndoRedo manager"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	# Get global undo/redo history (shared across all editor operations)
	var history = undo_redo.get_history_undo_redo(EditorUndoRedoManager.GLOBAL_HISTORY)

	if not history or not history.has_undo():
		return {"result": {
			"success": false,
			"message": "Nothing to undo"
		}}

	# Note: UndoRedo.undo() does not return a success indicator in Godot's API
	# We verify has_undo() beforehand and trust the operation succeeds
	history.undo()

	return {"result": {
		"success": true,
		"message": "Undo performed",
		"note": "Undo success cannot be verified programmatically due to Godot API limitations"
	}}

func handle_redo(_params: Dictionary) -> Dictionary:
	var undo_redo = _get_undo_redo()
	if not undo_redo:
		return {"error": "Could not access UndoRedo manager"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var history = undo_redo.get_history_undo_redo(EditorUndoRedoManager.GLOBAL_HISTORY)

	if not history or not history.has_redo():
		return {"result": {
			"success": false,
			"message": "Nothing to redo"
		}}

	# Note: UndoRedo.redo() does not return a success indicator in Godot's API
	# We verify has_redo() beforehand and trust the operation succeeds
	history.redo()

	return {"result": {
		"success": true,
		"message": "Redo performed",
		"note": "Redo success cannot be verified programmatically due to Godot API limitations"
	}}

func handle_get_history(_params: Dictionary) -> Dictionary:
	var undo_redo = _get_undo_redo()
	if not undo_redo:
		return {"error": "Could not access UndoRedo manager"}

	var history = undo_redo.get_history_undo_redo(EditorUndoRedoManager.GLOBAL_HISTORY)

	if not history:
		return {"result": {
			"has_undo": false,
			"has_redo": false,
			"current_action": "",
			"version": 0
		}}

	return {"result": {
		"has_undo": history.has_undo(),
		"has_redo": history.has_redo(),
		"current_action": history.get_current_action_name(),
		"version": history.get_version()
	}}

func handle_clear_history(params: Dictionary) -> Dictionary:
	var confirm = params.get("confirm", false)

	if not confirm:
		return {"error": "Must set confirm to true to clear history"}

	var undo_redo = _get_undo_redo()
	if not undo_redo:
		return {"error": "Could not access UndoRedo manager"}

	# Clear global history (shared across all editor operations)
	undo_redo.clear_history(true, EditorUndoRedoManager.GLOBAL_HISTORY)

	return {"result": {
		"success": true,
		"message": "Undo history cleared"
	}}
