@tool
extends Node
## Task Handler for MCP async operations
##
## Handles job-related requests from MCP server.


func _get_job_manager() -> Node:
	var plugin = Engine.get_meta("GodotMCPPlugin") if Engine.has_meta("GodotMCPPlugin") else null
	if not plugin:
		push_warning("[Godot MCP] Plugin not found in Engine meta")
		return null
	var manager = plugin.get_job_manager()
	if not manager:
		push_warning("[Godot MCP] Job manager not available from plugin")
	return manager


func handle_list(params: Dictionary) -> Dictionary:
	var job_manager = _get_job_manager()
	if not job_manager:
		return {"error": "Job manager not available"}

	var jobs = job_manager.list_jobs()
	return {"result": {"jobs": jobs, "count": jobs.size()}}


func handle_get(params: Dictionary) -> Dictionary:
	var job_id = params.get("job_id", "")

	if job_id.is_empty():
		return {"error": "Job ID is required"}

	var job_manager = _get_job_manager()
	if not job_manager:
		return {"error": "Job manager not available"}

	var job = job_manager.get_job(job_id)
	if job.is_empty():
		return {"error": "Job not found: %s" % job_id}

	job["id"] = job_id
	return {"result": job}


func handle_cancel(params: Dictionary) -> Dictionary:
	var job_id = params.get("job_id", "")

	if job_id.is_empty():
		return {"error": "Job ID is required"}

	var job_manager = _get_job_manager()
	if not job_manager:
		return {"error": "Job manager not available"}

	var cancelled = job_manager.cancel_job(job_id)

	return {"result": {"job_id": job_id, "cancelled": cancelled}}


func handle_get_result(params: Dictionary) -> Dictionary:
	var job_id = params.get("job_id", "")

	if job_id.is_empty():
		return {"error": "Job ID is required"}

	var job_manager = _get_job_manager()
	if not job_manager:
		return {"error": "Job manager not available"}

	var job = job_manager.get_job(job_id)
	if job.is_empty():
		return {"error": "Job not found: %s" % job_id}

	if job.get("status", "") != "completed":
		return {"error": "Job not completed: %s (status: %s)" % [job_id, job.get("status", "unknown")]}

	return {"result": job.get("result", null)}
