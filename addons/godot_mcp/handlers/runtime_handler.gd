@tool
extends Node

var _is_running := false
var _debug_output: Array[String] = []
var _running_scene_path := ""

func handle_run_project(params: Dictionary) -> Dictionary:
	var debug = params.get("debug", true)

	# Play the project
	EditorInterface.play_main_scene()

	_is_running = true
	_running_scene_path = ProjectSettings.get_setting("application/run/main_scene", "")
	_debug_output.clear()

	return {"result": {
		"running": true,
		"scene": _running_scene_path,
		"debug": debug
	}}

func handle_run_scene(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var debug = params.get("debug", true)

	if path.is_empty():
		return {"error": "Scene path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Scene file not found: %s" % path}

	# Play the specific scene
	EditorInterface.play_custom_scene(path)

	_is_running = true
	_running_scene_path = path
	_debug_output.clear()

	return {"result": {
		"running": true,
		"scene": path,
		"debug": debug
	}}

func handle_stop(_params: Dictionary) -> Dictionary:
	if not EditorInterface.is_playing_scene():
		return {"result": {
			"was_running": false,
			"message": "No project was running"
		}}

	EditorInterface.stop_playing_scene()
	_is_running = false
	_running_scene_path = ""

	return {"result": {
		"was_running": true,
		"stopped": true
	}}

func handle_status(_params: Dictionary) -> Dictionary:
	var is_playing = EditorInterface.is_playing_scene()

	return {"result": {
		"running": is_playing,
		"scene": _running_scene_path if is_playing else "",
		"debug_lines": _debug_output.size()
	}}

func handle_get_debug_output(params: Dictionary) -> Dictionary:
	var lines = params.get("lines", 100)

	var output = _debug_output
	if output.size() > lines:
		output = output.slice(-lines)

	return {"result": {
		"output": output,
		"total_lines": _debug_output.size(),
		"returned_lines": output.size()
	}}

func handle_reload_scene(_params: Dictionary) -> Dictionary:
	if not EditorInterface.is_playing_scene():
		return {"error": "No project is currently running"}

	# Get current scene and restart
	var current_scene = _running_scene_path
	EditorInterface.stop_playing_scene()

	# Schedule restart with delay using a timer callback (fire-and-forget)
	# Cannot use await here because Object.call() dispatch doesn't support coroutines
	_schedule_scene_restart(current_scene)

	return {"result": {
		"reloaded": true,
		"scene": current_scene
	}}

func _schedule_scene_restart(scene_path: String) -> void:
	var tree = get_tree()
	if not tree:
		push_warning("[Godot MCP] Scene tree not available, restarting immediately")
		if scene_path.is_empty():
			EditorInterface.play_main_scene()
		else:
			EditorInterface.play_custom_scene(scene_path)
		return

	var timer = tree.create_timer(0.1)
	timer.timeout.connect(func():
		if scene_path.is_empty():
			EditorInterface.play_main_scene()
		else:
			EditorInterface.play_custom_scene(scene_path)
	)

# Called to add debug output (can be connected to Engine.print signals if needed)
func _add_debug_output(message: String) -> void:
	_debug_output.append(message)
	# Keep only last 1000 lines
	if _debug_output.size() > 1000:
		_debug_output = _debug_output.slice(-1000)

func _get_script_editor() -> ScriptEditor:
	return EditorInterface.get_script_editor()

func handle_set_breakpoint(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var line = params.get("line", 1)
	var enabled = params.get("enabled", true)

	if path.is_empty():
		return {"error": "Script path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not FileAccess.file_exists(path):
		return {"error": "Script file not found: %s" % path}

	# Note: In Godot 4.x, breakpoint management is done through EditorDebuggerPlugin
	# which is not directly accessible. The ScriptEditor breakpoints are for display only.
	# This provides a best-effort implementation.

	var script_editor = _get_script_editor()
	if not script_editor:
		return {"error": "Script editor not available"}

	# Load and open the script
	var script = load(path)
	if not script:
		return {"error": "Failed to load script: %s" % path}

	# Open the script in the editor (this ensures breakpoints can be set)
	EditorInterface.edit_script(script, line - 1, 0)

	return {"result": {
		"path": path,
		"line": line,
		"enabled": enabled,
		"script_opened": true,
		"limitation": "Breakpoint toggling requires EditorDebuggerPlugin. Script was opened at the specified line - set breakpoint manually if needed."
	}}

func handle_is_debugger_active(_params: Dictionary) -> Dictionary:
	# Check if a scene is running (which means debugger can be active)
	var is_playing = EditorInterface.is_playing_scene()

	return {"result": {
		"active": is_playing,
		"message": "Debugger is active when a scene is running in debug mode"
	}}

func handle_is_debugger_breaked(_params: Dictionary) -> Dictionary:
	# Note: Direct access to debugger break state requires EditorDebuggerPlugin
	# This is a best-effort implementation
	var is_playing = EditorInterface.is_playing_scene()

	return {"result": {
		"scene_running": is_playing,
		"breaked": null,
		"limitation": "Actual break state detection requires EditorDebuggerPlugin access. Check the Debugger panel in the editor."
	}}

func handle_toggle_profiler(params: Dictionary) -> Dictionary:
	var enabled = params.get("enabled", true)

	# Note: Profiler control is typically done through the editor UI
	# or via EditorDebuggerPlugin in custom implementations

	return {"result": {
		"requested_state": enabled,
		"applied": false,
		"limitation": "Profiler control requires EditorDebuggerPlugin or editor UI. Use Debugger > Profiler tab in the editor."
	}}
