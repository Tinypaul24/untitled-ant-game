@tool
extends Node

## Animation handler for Godot MCP plugin
##
## Provides tools for controlling AnimationPlayer nodes in the editor.
## Compatible with Godot 4.6+ API.

func _get_edited_scene_root() -> Node:
	var plugin = Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null
	if not plugin:
		return null
	var editor_interface = plugin.get_editor_interface()
	return editor_interface.get_edited_scene_root()

func _find_node_by_path(root: Node, path: String) -> Node:
	if path == "." or path == root.name:
		return root
	if path.begins_with("./"):
		path = path.substr(2)
	return root.get_node_or_null(path)

func _get_animation_player(node_path: String) -> AnimationPlayer:
	var root = _get_edited_scene_root()
	if not root:
		return null

	var node = _find_node_by_path(root, node_path)
	if not node:
		return null

	if node is AnimationPlayer:
		return node

	return null

## Plays an animation on an AnimationPlayer node.
##
## Supports both forward and backward playback with custom speed.
## In Godot 4.6+, play_backwards() only accepts 2 parameters (name, blend).
## For backwards playback with custom speed, this handler uses play() with
## negative speed and from_end=true parameter.
##
## @param params.node_path: Path to the AnimationPlayer node
## @param params.animation_name: Name of the animation to play
## @param params.backwards: Whether to play backwards (default: false)
## @param params.custom_speed: Speed multiplier (default: 1.0)
## @return Dictionary with result or error
func handle_play(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var animation_name = params.get("animation_name", "")
	var backwards = params.get("backwards", false)
	var custom_speed = params.get("custom_speed", 1.0)

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if animation_name.is_empty():
		return {"error": "Animation name is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	if not player.has_animation(animation_name):
		return {"error": "Animation not found: %s" % animation_name}

	if backwards:
		# Godot 4.6: play_backwards() only accepts 2 parameters (name, blend)
		# Use play() with negative speed and from_end=true for custom speed
		player.play(animation_name, -1, custom_speed * -1.0, true)
	else:
		player.play(animation_name, -1, custom_speed)

	return {"result": {
		"node_path": str(player.get_path()),
		"animation": animation_name,
		"backwards": backwards,
		"speed": custom_speed,
		"playing": true
	}}

func handle_stop(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var keep_state = params.get("keep_state", false)

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	var was_playing = player.is_playing()
	var current_animation = player.current_animation

	player.stop(keep_state)

	return {"result": {
		"node_path": str(player.get_path()),
		"was_playing": was_playing,
		"stopped_animation": current_animation,
		"keep_state": keep_state
	}}

func handle_pause(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var paused = params.get("paused", true)
	var restore_speed = params.get("restore_speed", -1.0)  # -1 means use default (1.0)

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	# AnimationPlayer doesn't have a direct pause method
	# We use the speed_scale or process mode
	var original_speed_scale = player.speed_scale
	if paused:
		# Only warn if using a custom speed (not 0.0 or 1.0)
		if player.speed_scale != 0.0 and player.speed_scale != 1.0:
			push_warning("[Godot MCP] Pausing animation resets speed_scale to 0 (was: %f)" % player.speed_scale)
		player.speed_scale = 0.0
	else:
		# Use restore_speed if provided, otherwise default to 1.0
		if restore_speed > 0:
			player.speed_scale = restore_speed
		else:
			player.speed_scale = 1.0

	var result = {
		"node_path": str(player.get_path()),
		"paused": paused,
		"current_animation": player.current_animation,
		"speed_scale": player.speed_scale
	}
	# Include original speed_scale when pausing so caller can restore it
	if paused and original_speed_scale != 0.0 and original_speed_scale != 1.0:
		result["original_speed_scale"] = original_speed_scale
		result["note"] = "Original speed_scale was %f. Pass restore_speed=%f when unpausing to restore." % [original_speed_scale, original_speed_scale]

	return {"result": result}

func handle_get_animations(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	var animations = player.get_animation_list()
	var result = []
	var warnings = []

	for anim_name in animations:
		var anim = player.get_animation(anim_name)
		if not anim:
			push_warning("[Godot MCP] Could not retrieve animation: %s" % anim_name)
			warnings.append("Could not retrieve animation: %s" % anim_name)
			continue
		result.append({
			"name": anim_name,
			"length": anim.length,
			"loop_mode": anim.loop_mode,
			"step": anim.step
		})

	var response = {
		"node_path": str(player.get_path()),
		"animations": result,
		"count": result.size(),
		"current_animation": player.current_animation,
		"is_playing": player.is_playing()
	}

	if not warnings.is_empty():
		response["warnings"] = warnings

	return {"result": response}

func handle_seek(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var position = params.get("position", 0.0)
	var update = params.get("update", true)

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	player.seek(position, update)

	return {"result": {
		"node_path": str(player.get_path()),
		"position": position,
		"current_animation": player.current_animation,
		"updated": update
	}}

func _get_animation_library(player: AnimationPlayer, library_name: String) -> AnimationLibrary:
	if player.has_animation_library(library_name):
		return player.get_animation_library(library_name)
	return null

func handle_add_animation(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var animation_name = params.get("animation_name", "")
	var length = params.get("length", 1.0)
	var loop_mode = params.get("loop_mode", 0)
	var step = params.get("step", 0.0333)

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if animation_name.is_empty():
		return {"error": "Animation name is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	# Get or create default animation library
	var library = _get_animation_library(player, "")
	if not library:
		library = AnimationLibrary.new()
		player.add_animation_library("", library)

	if library.has_animation(animation_name):
		return {"error": "Animation already exists: %s" % animation_name}

	# Create animation resource
	var animation = Animation.new()
	animation.length = length
	animation.loop_mode = loop_mode
	animation.step = step

	var err = library.add_animation(animation_name, animation)
	if err != OK:
		return {"error": "Failed to add animation: %s (error: %d)" % [animation_name, err]}

	return {"result": {
		"node_path": str(player.get_path()),
		"animation_name": animation_name,
		"length": length,
		"loop_mode": loop_mode,
		"step": step,
		"created": true
	}}

func handle_remove_animation(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var animation_name = params.get("animation_name", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if animation_name.is_empty():
		return {"error": "Animation name is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	var library = _get_animation_library(player, "")
	if not library or not library.has_animation(animation_name):
		return {"error": "Animation not found: %s" % animation_name}

	library.remove_animation(animation_name)

	return {"result": {
		"node_path": str(player.get_path()),
		"animation_name": animation_name,
		"removed": true,
		"remaining_count": player.get_animation_list().size()
	}}

func handle_rename_animation(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var old_name = params.get("old_name", "")
	var new_name = params.get("new_name", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if old_name.is_empty() or new_name.is_empty():
		return {"error": "Both old_name and new_name are required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	var library = _get_animation_library(player, "")
	if not library or not library.has_animation(old_name):
		return {"error": "Animation not found: %s" % old_name}

	if library.has_animation(new_name):
		return {"error": "Animation name already in use: %s" % new_name}

	library.rename_animation(old_name, new_name)

	return {"result": {
		"node_path": str(player.get_path()),
		"old_name": old_name,
		"new_name": new_name,
		"renamed": true
	}}

func handle_add_track(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var animation_name = params.get("animation_name", "")
	var track_type = params.get("track_type", 0)
	var track_path = params.get("track_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if animation_name.is_empty():
		return {"error": "Animation name is required"}

	if track_path.is_empty():
		return {"error": "Track path is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	if not player.has_animation(animation_name):
		return {"error": "Animation not found: %s" % animation_name}

	var animation = player.get_animation(animation_name)
	if not animation:
		return {"error": "Could not retrieve animation: %s" % animation_name}

	var track_index = animation.add_track(track_type)
	animation.track_set_path(track_index, NodePath(track_path))

	return {"result": {
		"node_path": str(player.get_path()),
		"animation_name": animation_name,
		"track_index": track_index,
		"track_type": track_type,
		"track_path": track_path,
		"total_tracks": animation.get_track_count()
	}}

func handle_remove_track(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")
	var animation_name = params.get("animation_name", "")
	var track_index = params.get("track_index", -1)

	if node_path.is_empty():
		return {"error": "Node path is required"}

	if animation_name.is_empty():
		return {"error": "Animation name is required"}

	if track_index < 0:
		return {"error": "Track index is required"}

	var player = _get_animation_player(node_path)
	if not player:
		return {"error": "AnimationPlayer not found at: %s" % node_path}

	if not player.has_animation(animation_name):
		return {"error": "Animation not found: %s" % animation_name}

	var animation = player.get_animation(animation_name)
	if not animation:
		return {"error": "Could not retrieve animation: %s" % animation_name}

	if track_index >= animation.get_track_count():
		return {"error": "Track index out of range: %d (track count: %d)" % [track_index, animation.get_track_count()]}

	var track_path = str(animation.track_get_path(track_index))
	var track_type = animation.track_get_type(track_index)
	animation.remove_track(track_index)

	return {"result": {
		"node_path": str(player.get_path()),
		"animation_name": animation_name,
		"removed_track_index": track_index,
		"removed_track_path": track_path,
		"removed_track_type": track_type,
		"remaining_tracks": animation.get_track_count()
	}}
