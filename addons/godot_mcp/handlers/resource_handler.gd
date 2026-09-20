@tool
extends Node

func _get_resource_previewer() -> EditorResourcePreview:
	return EditorInterface.get_resource_previewer()

func _get_job_manager() -> Node:
	var plugin = Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null
	if not plugin:
		push_warning("[Godot MCP] Plugin not found in Engine meta")
		return null
	var manager = plugin.get_job_manager()
	if not manager:
		push_warning("[Godot MCP] Job manager not available from plugin")
	return manager

func handle_exists(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var type_hint = params.get("type_hint", "")

	if path.is_empty():
		return {"error": "Resource path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	var exists = ResourceLoader.exists(path, type_hint)

	return {"result": {
		"path": path,
		"exists": exists,
		"type_hint": type_hint if not type_hint.is_empty() else null
	}}

func handle_get_preview(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")
	var async_mode = params.get("async", true)

	if path.is_empty():
		return {"error": "Resource path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not ResourceLoader.exists(path):
		return {"error": "Resource not found: %s" % path}

	var previewer = _get_resource_previewer()
	if not previewer:
		return {"error": "Could not access EditorResourcePreview"}

	var job_manager = _get_job_manager()
	if not job_manager:
		return {"error": "Job manager not available for async preview"}

	# Create a job for tracking this preview generation
	var job_id = job_manager.create_job("resource.get_preview", {"path": path})
	job_manager.start_job(job_id)

	# Queue the preview request with callback
	previewer.queue_resource_preview(path, self, "_on_preview_generated", job_id)

	return {"result": {
		"job_id": job_id,
		"status": "pending",
		"message": "Preview generation started. Use task.get to check status."
	}}


func _on_preview_generated(path: String, preview: Texture2D, thumbnail_preview: Texture2D, userdata: Variant) -> void:
	var job_id = userdata as String
	var job_manager = _get_job_manager()
	if not job_manager:
		push_error("[Godot MCP] _on_preview_generated: job_manager unavailable, cannot complete job %s for path %s" % [job_id, path])
		return

	if preview == null:
		push_warning("[Godot MCP] Preview generation failed for: %s (job: %s)" % [path, job_id])
		job_manager.fail_job(job_id, "Failed to generate preview for: %s" % path)
		return

	# Convert preview to PNG and encode as base64
	var image = preview.get_image()
	if image == null:
		job_manager.fail_job(job_id, "Failed to get image from preview texture")
		return

	var png_data = image.save_png_to_buffer()
	var base64_data = Marshalls.raw_to_base64(png_data)

	var result = {
		"path": path,
		"preview": {
			"format": "png",
			"encoding": "base64",
			"data": base64_data,
			"width": image.get_width(),
			"height": image.get_height()
		}
	}

	# Include thumbnail if available
	if thumbnail_preview != null:
		var thumb_image = thumbnail_preview.get_image()
		if thumb_image != null:
			var thumb_png = thumb_image.save_png_to_buffer()
			result["thumbnail"] = {
				"format": "png",
				"encoding": "base64",
				"data": Marshalls.raw_to_base64(thumb_png),
				"width": thumb_image.get_width(),
				"height": thumb_image.get_height()
			}

	job_manager.complete_job(job_id, result)

func handle_check_invalidation(params: Dictionary) -> Dictionary:
	var path = params.get("path", "")

	if path.is_empty():
		return {"error": "Resource path is required"}

	if not path.begins_with("res://"):
		path = "res://" + path

	if not ResourceLoader.exists(path):
		return {"error": "Resource not found: %s" % path}

	var previewer = _get_resource_previewer()
	if not previewer:
		return {"error": "Could not access EditorResourcePreview"}

	previewer.check_for_invalidation(path)

	return {"result": {
		"path": path,
		"message": "Invalidation check initiated",
		"note": "If the resource was modified, its preview will be regenerated"
	}}
