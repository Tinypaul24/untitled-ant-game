@tool
extends Node

func _save_bus_layout() -> void:
	var layout = AudioServer.generate_bus_layout()
	AudioServer.set_bus_layout(layout)
	var layout_path = ProjectSettings.get_setting("audio/buses/default_bus_layout", "res://default_bus_layout.tres")
	var err = ResourceSaver.save(layout, layout_path)
	if err != OK:
		push_error("[Godot MCP] Failed to save bus layout to %s: error code %d" % [layout_path, err])

func _get_bus_index(bus_name: String) -> int:
	for i in range(AudioServer.bus_count):
		if AudioServer.get_bus_name(i) == bus_name:
			return i
	return -1

func handle_get_buses(_params: Dictionary) -> Dictionary:
	var buses = []

	for i in range(AudioServer.bus_count):
		var bus_name = AudioServer.get_bus_name(i)
		var effects = []

		for j in range(AudioServer.get_bus_effect_count(i)):
			var effect = AudioServer.get_bus_effect(i, j)
			if not effect:
				push_warning("[Godot MCP] Could not retrieve audio effect at bus %d, index %d" % [i, j])
				continue
			effects.append({
				"index": j,
				"type": effect.get_class(),
				"enabled": AudioServer.is_bus_effect_enabled(i, j)
			})

		buses.append({
			"index": i,
			"name": bus_name,
			"volume_db": AudioServer.get_bus_volume_db(i),
			"muted": AudioServer.is_bus_mute(i),
			"solo": AudioServer.is_bus_solo(i),
			"send": AudioServer.get_bus_send(i),
			"effects": effects
		})

	return {"result": {
		"buses": buses,
		"count": buses.size()
	}}

func handle_set_volume(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var volume_db = params.get("volume_db", 0.0)

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	var old_volume = AudioServer.get_bus_volume_db(bus_index)
	AudioServer.set_bus_volume_db(bus_index, volume_db)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"bus_index": bus_index,
		"old_volume_db": old_volume,
		"new_volume_db": volume_db,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_set_mute(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var muted = params.get("muted", true)

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	var was_muted = AudioServer.is_bus_mute(bus_index)
	AudioServer.set_bus_mute(bus_index, muted)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"bus_index": bus_index,
		"was_muted": was_muted,
		"muted": muted,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_add_effect(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var effect_type = params.get("effect_type", "")
	var at_position = params.get("at_position", -1)

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	if effect_type.is_empty():
		return {"error": "Effect type is required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	# Validate effect type exists
	if not ClassDB.class_exists(effect_type):
		return {"error": "Invalid effect type: %s" % effect_type}

	if not ClassDB.is_parent_class(effect_type, "AudioEffect"):
		return {"error": "Type is not an AudioEffect: %s" % effect_type}

	# Create the effect
	var effect = ClassDB.instantiate(effect_type)
	if not effect:
		return {"error": "Failed to create effect: %s" % effect_type}
	effect.resource_name = effect_type

	# Determine position
	var position = at_position
	if position < 0:
		position = AudioServer.get_bus_effect_count(bus_index)

	AudioServer.add_bus_effect(bus_index, effect, position)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"bus_index": bus_index,
		"effect_type": effect_type,
		"effect_index": position,
		"total_effects": AudioServer.get_bus_effect_count(bus_index),
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_add_bus(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var at_position = params.get("at_position", -1)

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	# Check if bus name already exists
	if _get_bus_index(bus_name) >= 0:
		return {"error": "Audio bus already exists: %s" % bus_name}

	AudioServer.add_bus(at_position)

	# The new bus is at the specified position or the end
	var new_index = at_position if at_position >= 0 else AudioServer.bus_count - 1
	AudioServer.set_bus_name(new_index, bus_name)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"bus_index": new_index,
		"total_buses": AudioServer.bus_count,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_remove_bus(params: Dictionary) -> Dictionary:
	var bus_index = params.get("bus_index", -1)

	if bus_index < 0:
		return {"error": "Bus index is required"}

	if bus_index == 0:
		return {"error": "Cannot remove the Master bus (index 0)"}

	if bus_index >= AudioServer.bus_count:
		return {"error": "Bus index out of range: %d (bus count: %d)" % [bus_index, AudioServer.bus_count]}

	var removed_name = AudioServer.get_bus_name(bus_index)
	AudioServer.remove_bus(bus_index)
	_save_bus_layout()

	return {"result": {
		"removed_bus": removed_name,
		"removed_index": bus_index,
		"total_buses": AudioServer.bus_count,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_move_bus(params: Dictionary) -> Dictionary:
	var bus_index = params.get("bus_index", -1)
	var to_index = params.get("to_index", -1)

	if bus_index < 0 or to_index < 0:
		return {"error": "Both bus_index and to_index are required"}

	if bus_index >= AudioServer.bus_count:
		return {"error": "Source bus index out of range: %d" % bus_index}

	if to_index >= AudioServer.bus_count:
		return {"error": "Target index out of range: %d" % to_index}

	var bus_name = AudioServer.get_bus_name(bus_index)
	AudioServer.move_bus(bus_index, to_index)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"old_index": bus_index,
		"new_index": to_index,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_set_bus_name(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var new_name = params.get("new_name", "")

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	if new_name.is_empty():
		return {"error": "New name is required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	# Check if new name already exists
	if _get_bus_index(new_name) >= 0:
		return {"error": "Audio bus name already in use: %s" % new_name}

	AudioServer.set_bus_name(bus_index, new_name)
	_save_bus_layout()

	return {"result": {
		"old_name": bus_name,
		"new_name": new_name,
		"bus_index": bus_index,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_remove_effect(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var effect_index = params.get("effect_index", -1)

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	if effect_index < 0:
		return {"error": "Effect index is required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	if effect_index >= AudioServer.get_bus_effect_count(bus_index):
		return {"error": "Effect index out of range: %d (effect count: %d)" % [effect_index, AudioServer.get_bus_effect_count(bus_index)]}

	var effect = AudioServer.get_bus_effect(bus_index, effect_index)
	var effect_type = effect.get_class() if effect else "unknown"

	AudioServer.remove_bus_effect(bus_index, effect_index)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"bus_index": bus_index,
		"removed_effect_type": effect_type,
		"removed_index": effect_index,
		"total_effects": AudioServer.get_bus_effect_count(bus_index),
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_set_effect_enabled(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var effect_index = params.get("effect_index", -1)
	var enabled = params.get("enabled", true)

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	if effect_index < 0:
		return {"error": "Effect index is required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	if effect_index >= AudioServer.get_bus_effect_count(bus_index):
		return {"error": "Effect index out of range: %d" % effect_index}

	var was_enabled = AudioServer.is_bus_effect_enabled(bus_index, effect_index)
	AudioServer.set_bus_effect_enabled(bus_index, effect_index, enabled)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"effect_index": effect_index,
		"was_enabled": was_enabled,
		"enabled": enabled,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_set_send(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var send_to = params.get("send_to", "")

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	if send_to.is_empty():
		return {"error": "Send target is required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	# Validate target bus exists
	if _get_bus_index(send_to) < 0:
		return {"error": "Target audio bus not found: %s" % send_to}

	var old_send = AudioServer.get_bus_send(bus_index)
	AudioServer.set_bus_send(bus_index, send_to)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"old_send": old_send,
		"new_send": send_to,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_set_solo(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var solo = params.get("solo", true)

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	var was_solo = AudioServer.is_bus_solo(bus_index)
	AudioServer.set_bus_solo(bus_index, solo)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"bus_index": bus_index,
		"was_solo": was_solo,
		"solo": solo,
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}

func handle_swap_effects(params: Dictionary) -> Dictionary:
	var bus_name = params.get("bus_name", "")
	var effect_index_a = params.get("effect_index_a", -1)
	var effect_index_b = params.get("effect_index_b", -1)

	if bus_name.is_empty():
		return {"error": "Bus name is required"}

	if effect_index_a < 0 or effect_index_b < 0:
		return {"error": "Both effect indices are required"}

	var bus_index = _get_bus_index(bus_name)
	if bus_index < 0:
		return {"error": "Audio bus not found: %s" % bus_name}

	var effect_count = AudioServer.get_bus_effect_count(bus_index)
	if effect_index_a >= effect_count or effect_index_b >= effect_count:
		return {"error": "Effect index out of range (effect count: %d)" % effect_count}

	AudioServer.swap_bus_effects(bus_index, effect_index_a, effect_index_b)
	_save_bus_layout()

	return {"result": {
		"bus_name": bus_name,
		"swapped": [effect_index_a, effect_index_b],
		"editor_note": "Changes saved to default_bus_layout.tres. Click 'Load Default' in Audio tab to refresh editor UI."
	}}
