@tool
extends Node

func _get_base_control() -> Control:
	return EditorInterface.get_base_control()

func handle_get_editor_theme(_params: Dictionary) -> Dictionary:
	var base_control = _get_base_control()
	if not base_control:
		return {"error": "Could not access editor base control"}

	# Get common theme colors using Control instance methods
	# which traverse the theme inheritance chain
	var colors = {}
	var color_names = [
		"accent_color", "font_color", "font_readonly_color", "font_placeholder_color",
		"error_color", "warning_color", "success_color", "selection_color",
		"highlight_color", "disabled_font_color"
	]

	for color_name in color_names:
		if base_control.has_theme_color(color_name, "Editor"):
			var color = base_control.get_theme_color(color_name, "Editor")
			colors[color_name] = {
				"r": color.r,
				"g": color.g,
				"b": color.b,
				"a": color.a,
				"hex": color.to_html(true)
			}

	return {"result": {
		"theme_available": true,
		"colors": colors
	}}

func handle_get_color(params: Dictionary) -> Dictionary:
	var color_name = params.get("color_name", "")
	var node_type = params.get("node_type", "Editor")

	if color_name.is_empty():
		return {"error": "Color name is required"}

	var base_control = _get_base_control()
	if not base_control:
		return {"error": "Could not access editor base control"}

	if not base_control.has_theme_color(color_name, node_type):
		# Try to find it in EditorIcons or other common types
		var types_to_try = ["Editor", "EditorIcons", "EditorStyles", node_type]
		var found = false
		for try_type in types_to_try:
			if base_control.has_theme_color(color_name, try_type):
				node_type = try_type
				found = true
				break

		if not found:
			return {"error": "Color not found: %s (in type: %s)" % [color_name, node_type]}

	var color = base_control.get_theme_color(color_name, node_type)

	return {"result": {
		"color_name": color_name,
		"node_type": node_type,
		"color": {
			"r": color.r,
			"g": color.g,
			"b": color.b,
			"a": color.a,
			"hex": color.to_html(true)
		}
	}}

func handle_get_icon(params: Dictionary) -> Dictionary:
	var icon_name = params.get("icon_name", "")
	var node_type = params.get("node_type", "EditorIcons")

	if icon_name.is_empty():
		return {"error": "Icon name is required"}

	var base_control = _get_base_control()
	if not base_control:
		return {"error": "Could not access editor base control"}

	if not base_control.has_theme_icon(icon_name, node_type):
		# Try EditorIcons first
		if base_control.has_theme_icon(icon_name, "EditorIcons"):
			node_type = "EditorIcons"
		else:
			return {"error": "Icon not found: %s (in type: %s)" % [icon_name, node_type]}

	var icon = base_control.get_theme_icon(icon_name, node_type)
	if not icon:
		return {"error": "Failed to get icon: %s" % icon_name}

	return {"result": {
		"icon_name": icon_name,
		"node_type": node_type,
		"width": icon.get_width(),
		"height": icon.get_height()
	}}
