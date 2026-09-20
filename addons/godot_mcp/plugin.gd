@tool
extends EditorPlugin

const PORT = 6505

var tcp_server := TCPServer.new()
var clients := {}
var next_client_id := 1
var command_handlers := {}
var _job_manager: Node = null

signal client_connected(id: int)
signal client_disconnected(id: int)
signal command_received(client_id: int, command: Dictionary)

class WebSocketClient:
	var tcp: StreamPeerTCP
	var id: int
	var ws: WebSocketPeer
	var state: int = -1  # -1: handshaking, 0: connected, 1: closed
	var handshake_time: int

	func _init(p_tcp: StreamPeerTCP, p_id: int) -> void:
		tcp = p_tcp
		id = p_id
		handshake_time = Time.get_ticks_msec()

	func upgrade_to_websocket() -> bool:
		ws = WebSocketPeer.new()
		return ws.accept_stream(tcp) == OK

func _enter_tree() -> void:
	Engine.set_meta("GodotMCPPlugin", self)
	_setup_job_manager()
	_register_handlers()
	_start_server()


func _setup_job_manager() -> void:
	var job_manager_path = "res://addons/godot_mcp/job_manager.gd"
	if ResourceLoader.exists(job_manager_path):
		_job_manager = load(job_manager_path).new()
		_job_manager.name = "JobManager"
		add_child(_job_manager)
		print("[Godot MCP] Job manager initialized")


func get_job_manager() -> Node:
	return _job_manager

func _exit_tree() -> void:
	if Engine.has_meta("GodotMCPPlugin"):
		Engine.remove_meta("GodotMCPPlugin")
	_stop_server()
	if _job_manager:
		_job_manager.queue_free()
		_job_manager = null

func _register_handlers() -> void:
	# Register command handlers
	var handlers_path = "res://addons/godot_mcp/handlers/"

	# Project handler
	if ResourceLoader.exists(handlers_path + "project_handler.gd"):
		var handler = load(handlers_path + "project_handler.gd").new()
		handler.name = "ProjectHandler"
		add_child(handler)
		command_handlers["project"] = handler

	# Scene handler
	if ResourceLoader.exists(handlers_path + "scene_handler.gd"):
		var handler = load(handlers_path + "scene_handler.gd").new()
		handler.name = "SceneHandler"
		add_child(handler)
		command_handlers["scene"] = handler

	# Script handler
	if ResourceLoader.exists(handlers_path + "script_handler.gd"):
		var handler = load(handlers_path + "script_handler.gd").new()
		handler.name = "ScriptHandler"
		add_child(handler)
		command_handlers["script"] = handler

	# Node handler
	if ResourceLoader.exists(handlers_path + "node_handler.gd"):
		var handler = load(handlers_path + "node_handler.gd").new()
		handler.name = "NodeHandler"
		add_child(handler)
		command_handlers["node"] = handler

	# Runtime handler
	if ResourceLoader.exists(handlers_path + "runtime_handler.gd"):
		var handler = load(handlers_path + "runtime_handler.gd").new()
		handler.name = "RuntimeHandler"
		add_child(handler)
		command_handlers["runtime"] = handler

	# Selection handler
	if ResourceLoader.exists(handlers_path + "selection_handler.gd"):
		var handler = load(handlers_path + "selection_handler.gd").new()
		handler.name = "SelectionHandler"
		add_child(handler)
		command_handlers["selection"] = handler

	# ClassDB handler
	if ResourceLoader.exists(handlers_path + "classdb_handler.gd"):
		var handler = load(handlers_path + "classdb_handler.gd").new()
		handler.name = "ClassDBHandler"
		add_child(handler)
		command_handlers["classdb"] = handler

	# Undo/Redo handler
	if ResourceLoader.exists(handlers_path + "undoredo_handler.gd"):
		var handler = load(handlers_path + "undoredo_handler.gd").new()
		handler.name = "UndoRedoHandler"
		add_child(handler)
		command_handlers["undoredo"] = handler

	# FileSystem handler
	if ResourceLoader.exists(handlers_path + "filesystem_handler.gd"):
		var handler = load(handlers_path + "filesystem_handler.gd").new()
		handler.name = "FileSystemHandler"
		add_child(handler)
		command_handlers["filesystem"] = handler

	# Resource handler
	if ResourceLoader.exists(handlers_path + "resource_handler.gd"):
		var handler = load(handlers_path + "resource_handler.gd").new()
		handler.name = "ResourceHandler"
		add_child(handler)
		command_handlers["resource"] = handler

	# Animation handler
	if ResourceLoader.exists(handlers_path + "animation_handler.gd"):
		var handler = load(handlers_path + "animation_handler.gd").new()
		handler.name = "AnimationHandler"
		add_child(handler)
		command_handlers["animation"] = handler

	# Audio handler
	if ResourceLoader.exists(handlers_path + "audio_handler.gd"):
		var handler = load(handlers_path + "audio_handler.gd").new()
		handler.name = "AudioHandler"
		add_child(handler)
		command_handlers["audio"] = handler

	# Theme handler
	if ResourceLoader.exists(handlers_path + "theme_handler.gd"):
		var handler = load(handlers_path + "theme_handler.gd").new()
		handler.name = "ThemeHandler"
		add_child(handler)
		command_handlers["theme"] = handler

	# Task handler (for async job management)
	if ResourceLoader.exists(handlers_path + "task_handler.gd"):
		var handler = load(handlers_path + "task_handler.gd").new()
		handler.name = "TaskHandler"
		add_child(handler)
		command_handlers["task"] = handler

	print("[Godot MCP] Registered %d command handlers" % command_handlers.size())

func _start_server() -> void:
	var err = tcp_server.listen(PORT)
	if err == OK:
		print("[Godot MCP] Server started on port %d" % PORT)
		set_process(true)
	else:
		push_error("[Godot MCP] Failed to start server on port %d: %d" % [PORT, err])

func _stop_server() -> void:
	if tcp_server.is_listening():
		tcp_server.stop()
	for id in clients.keys():
		clients.erase(id)
	print("[Godot MCP] Server stopped")

func _process(_delta: float) -> void:
	if not tcp_server.is_listening():
		return

	# Accept new connections
	while tcp_server.is_connection_available():
		var tcp = tcp_server.take_connection()
		var id = next_client_id
		next_client_id += 1

		var client = WebSocketClient.new(tcp, id)
		clients[id] = client

		if client.upgrade_to_websocket():
			print("[Godot MCP] Client %d: WebSocket handshake started" % id)
		else:
			print("[Godot MCP] Client %d: Failed to start handshake" % id)
			clients.erase(id)

	# Process clients
	var current_time = Time.get_ticks_msec()
	var to_remove := []

	for id in clients:
		var client: WebSocketClient = clients[id]

		if client.state == -1:  # Handshaking
			if client.ws != null:
				client.ws.poll()
				var ws_state = client.ws.get_ready_state()

				if ws_state == WebSocketPeer.STATE_OPEN:
					print("[Godot MCP] Client %d: Connected" % id)
					client.state = 0
					client_connected.emit(id)
					_send_welcome(client)
				elif ws_state != WebSocketPeer.STATE_CONNECTING:
					to_remove.append(id)
				elif current_time - client.handshake_time > 5000:
					print("[Godot MCP] Client %d: Handshake timeout" % id)
					to_remove.append(id)

		elif client.state == 0:  # Connected
			client.ws.poll()

			var ws_state = client.ws.get_ready_state()
			if ws_state != WebSocketPeer.STATE_OPEN:
				print("[Godot MCP] Client %d: Disconnected" % id)
				client_disconnected.emit(id)
				to_remove.append(id)
				continue

			# Process messages
			while client.ws.get_available_packet_count() > 0:
				var packet = client.ws.get_packet()
				var text = packet.get_string_from_utf8()
				_handle_message(id, text)

	for id in to_remove:
		clients.erase(id)

func _send_welcome(client: WebSocketClient) -> void:
	var msg = JSON.stringify({
		"type": "welcome",
		"message": "Connected to Godot MCP Server",
		"version": "0.1.0"
	})
	client.ws.send_text(msg)

func _handle_message(client_id: int, text: String) -> void:
	var json = JSON.new()
	var parse_result = json.parse(text)

	if parse_result != OK:
		_send_error(client_id, "", "Invalid JSON: %s" % json.get_error_message())
		return

	var data = json.get_data()
	if not data is Dictionary:
		_send_error(client_id, "", "Expected JSON object")
		return

	var request_id = data.get("id", "")
	var method = data.get("method", "")
	var params = data.get("params", {})

	print("[Godot MCP] Client %d: Request %s - %s" % [client_id, request_id, method])

	# Route to appropriate handler
	var result = _route_command(method, params)

	if result.has("error"):
		_send_error(client_id, request_id, result.error)
	else:
		_send_result(client_id, request_id, result.get("result", {}))

func _route_command(method: String, params: Dictionary) -> Dictionary:
	var parts = method.split(".")
	if parts.size() < 2:
		return {"error": "Invalid method format. Expected: category.action"}

	var category = parts[0]
	var action = parts[1]

	# Handle built-in commands
	if category == "connection":
		if action == "ping":
			return {"result": {"status": "ok", "message": "pong"}}
		elif action == "status":
			return {"result": _get_status()}

	# Route to handler
	if command_handlers.has(category):
		var handler = command_handlers[category]
		if handler.has_method("handle_" + action):
			return handler.call("handle_" + action, params)
		else:
			return {"error": "Unknown action: %s.%s" % [category, action]}

	return {"error": "Unknown category: %s" % category}

func _get_status() -> Dictionary:
	var project_path = ProjectSettings.globalize_path("res://")
	var project_name = ProjectSettings.get_setting("application/config/name", "Unnamed Project")

	return {
		"status": "connected",
		"godot_version": Engine.get_version_info().string,
		"project_name": project_name,
		"project_path": project_path,
		"connected_clients": clients.size()
	}

func _send_result(client_id: int, request_id: String, result: Variant) -> void:
	var response = {
		"id": request_id,
		"result": result
	}
	_send_to_client(client_id, response)

func _send_error(client_id: int, request_id: String, message: String) -> void:
	var response = {
		"id": request_id,
		"error": {
			"code": -1,
			"message": message
		}
	}
	_send_to_client(client_id, response)

func _send_to_client(client_id: int, data: Dictionary) -> void:
	if not clients.has(client_id):
		return

	var client: WebSocketClient = clients[client_id]
	if client.ws.get_ready_state() == WebSocketPeer.STATE_OPEN:
		var json_text = JSON.stringify(data)
		client.ws.send_text(json_text)
