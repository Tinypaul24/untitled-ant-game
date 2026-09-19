@tool
extends Node

func _get_editor_selection() -> EditorSelection:
	return EditorInterface.get_selection()

func _get_edited_scene_root() -> Node:
	return EditorInterface.get_edited_scene_root()

func _find_node_by_path(root: Node, path: String) -> Node:
	if path == "." or path == root.name:
		return root
	if path.begins_with("./"):
		path = path.substr(2)
	return root.get_node_or_null(path)

func handle_get_selected(_params: Dictionary) -> Dictionary:
	var selection = _get_editor_selection()
	if not selection:
		return {"error": "Could not access EditorSelection"}

	var selected_nodes = selection.get_selected_nodes()
	var result = []

	for node in selected_nodes:
		result.append({
			"name": node.name,
			"type": node.get_class(),
			"path": str(node.get_path())
		})

	return {"result": {
		"selected_nodes": result,
		"count": result.size()
	}}

func handle_select(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var selection = _get_editor_selection()
	if not selection:
		return {"error": "Could not access EditorSelection"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	selection.add_node(node)

	return {"result": {
		"selected": str(node.get_path()),
		"node_name": node.name
	}}

func handle_deselect(params: Dictionary) -> Dictionary:
	var node_path = params.get("node_path", "")

	if node_path.is_empty():
		return {"error": "Node path is required"}

	var selection = _get_editor_selection()
	if not selection:
		return {"error": "Could not access EditorSelection"}

	var root = _get_edited_scene_root()
	if not root:
		return {"error": "No scene is currently being edited"}

	var node = _find_node_by_path(root, node_path)
	if not node:
		return {"error": "Node not found: %s" % node_path}

	selection.remove_node(node)

	return {"result": {
		"deselected": str(node.get_path()),
		"node_name": node.name
	}}

func handle_clear(_params: Dictionary) -> Dictionary:
	var selection = _get_editor_selection()
	if not selection:
		return {"error": "Could not access EditorSelection"}

	selection.clear()

	return {"result": {
		"cleared": true
	}}
