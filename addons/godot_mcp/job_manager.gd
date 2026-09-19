@tool
extends Node
## Job Manager for async operations
##
## Tracks long-running jobs (like preview generation) and notifies when complete.

signal job_completed(job_id: String, result: Variant)
signal job_failed(job_id: String, error: String)
signal job_progress(job_id: String, progress: float, message: String)

const JOB_TTL_MS := 300000  # 5 minutes

var _jobs := {}
var _next_job_id := 0
var _cleanup_timer: Timer = null


func _ready() -> void:
	# Setup cleanup timer
	_cleanup_timer = Timer.new()
	_cleanup_timer.name = "CleanupTimer"
	_cleanup_timer.wait_time = 60.0  # Check every minute
	_cleanup_timer.autostart = true
	_cleanup_timer.timeout.connect(_cleanup_old_jobs)
	add_child(_cleanup_timer)


func create_job(method: String, params: Dictionary) -> String:
	_next_job_id += 1
	var job_id = "job_%d_%d" % [Time.get_ticks_msec(), _next_job_id]

	_jobs[job_id] = {
		"status": "pending",
		"method": method,
		"params": params,
		"created_at": Time.get_ticks_msec(),
		"updated_at": Time.get_ticks_msec()
	}

	return job_id


func start_job(job_id: String) -> void:
	if _jobs.has(job_id):
		_jobs[job_id].status = "running"
		_jobs[job_id].updated_at = Time.get_ticks_msec()
	else:
		push_warning("[Godot MCP] start_job: job not found: %s" % job_id)


func complete_job(job_id: String, result: Variant) -> void:
	if _jobs.has(job_id):
		_jobs[job_id].status = "completed"
		_jobs[job_id].result = result
		_jobs[job_id].completed_at = Time.get_ticks_msec()
		_jobs[job_id].updated_at = Time.get_ticks_msec()
		job_completed.emit(job_id, result)
	else:
		push_warning("[Godot MCP] complete_job: job not found: %s" % job_id)


func fail_job(job_id: String, error: String) -> void:
	if _jobs.has(job_id):
		_jobs[job_id].status = "failed"
		_jobs[job_id].error = error
		_jobs[job_id].completed_at = Time.get_ticks_msec()
		_jobs[job_id].updated_at = Time.get_ticks_msec()
		job_failed.emit(job_id, error)
	else:
		push_warning("[Godot MCP] fail_job: job not found: %s" % job_id)


func update_progress(job_id: String, progress: float, message: String = "") -> void:
	if _jobs.has(job_id):
		_jobs[job_id].progress = progress
		_jobs[job_id].progress_message = message
		_jobs[job_id].updated_at = Time.get_ticks_msec()
		job_progress.emit(job_id, progress, message)
	else:
		push_warning("[Godot MCP] update_progress: job not found: %s" % job_id)


func get_job(job_id: String) -> Dictionary:
	if _jobs.has(job_id):
		return _jobs[job_id].duplicate()
	push_warning("[Godot MCP] get_job: job not found: %s" % job_id)
	return {}


func list_jobs() -> Array:
	var result = []
	for job_id in _jobs:
		var job = _jobs[job_id].duplicate()
		job["id"] = job_id
		result.append(job)
	return result


func cancel_job(job_id: String) -> bool:
	if not _jobs.has(job_id):
		push_warning("[Godot MCP] cancel_job: job not found: %s" % job_id)
		return false

	var job = _jobs[job_id]
	if job.status == "pending" or job.status == "running":
		job.status = "cancelled"
		job.completed_at = Time.get_ticks_msec()
		job.updated_at = Time.get_ticks_msec()
		return true

	push_warning("[Godot MCP] cancel_job: cannot cancel job %s (status: %s)" % [job_id, job.status])
	return false


func _cleanup_old_jobs() -> void:
	var now = Time.get_ticks_msec()
	var to_remove := []

	for job_id in _jobs:
		var job = _jobs[job_id]
		if job.status in ["completed", "failed", "cancelled"]:
			var completed_at = job.get("completed_at", job.created_at)
			if now - completed_at > JOB_TTL_MS:
				to_remove.append(job_id)

	for job_id in to_remove:
		_jobs.erase(job_id)

	if to_remove.size() > 0:
		print("[Godot MCP] Cleaned up %d old jobs" % to_remove.size())
