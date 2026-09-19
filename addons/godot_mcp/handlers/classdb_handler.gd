@tool
extends Node

func handle_get_class_list(params: Dictionary) -> Dictionary:
	var filter = params.get("filter", "")
	var all_classes = ClassDB.get_class_list()

	var result = []
	for cls_name in all_classes:
		if filter.is_empty() or cls_name.containsn(filter):
			result.append(cls_name)

	result.sort()

	return {"result": {
		"classes": result,
		"count": result.size()
	}}

func handle_get_class_hierarchy(params: Dictionary) -> Dictionary:
	var cls_name = params.get("class_name", "")

	if cls_name.is_empty():
		return {"error": "Class name is required"}

	if not ClassDB.class_exists(cls_name):
		return {"error": "Class not found: %s" % cls_name}

	var hierarchy = []
	var current = cls_name

	while not current.is_empty():
		hierarchy.append(current)
		current = ClassDB.get_parent_class(current)

	return {"result": {
		"class": cls_name,
		"hierarchy": hierarchy,
		"depth": hierarchy.size()
	}}

func handle_get_class_info(params: Dictionary) -> Dictionary:
	var cls_name = params.get("class_name", "")
	var include_methods = params.get("include_methods", false)
	var include_properties = params.get("include_properties", true)
	var include_signals = params.get("include_signals", true)

	if cls_name.is_empty():
		return {"error": "Class name is required"}

	if not ClassDB.class_exists(cls_name):
		return {"error": "Class not found: %s" % cls_name}

	var info = {
		"class_name": cls_name,
		"parent_class": ClassDB.get_parent_class(cls_name),
		"can_instantiate": ClassDB.can_instantiate(cls_name)
	}

	# Include methods if requested
	if include_methods:
		var methods = ClassDB.class_get_method_list(cls_name, true)
		var method_list = []
		for method in methods:
			method_list.append({
				"name": method["name"],
				"args": _format_args(method.get("args", [])),
				"return_type": _get_type_name(method.get("return", {}).get("type", TYPE_NIL))
			})
		info["methods"] = method_list

	# Include properties if requested
	if include_properties:
		var properties = ClassDB.class_get_property_list(cls_name, true)
		var prop_list = []
		for prop in properties:
			if not prop["name"].begins_with("_"):
				prop_list.append({
					"name": prop["name"],
					"type": _get_type_name(prop.get("type", TYPE_NIL)),
					"usage": prop.get("usage", 0)
				})
		info["properties"] = prop_list

	# Include signals if requested
	if include_signals:
		var signals = ClassDB.class_get_signal_list(cls_name, true)
		var signal_list = []
		for sig in signals:
			signal_list.append({
				"name": sig["name"],
				"args": _format_args(sig.get("args", []))
			})
		info["signals"] = signal_list

	return {"result": info}

func handle_get_inherited_classes(params: Dictionary) -> Dictionary:
	var cls_name = params.get("class_name", "")
	var direct_only = params.get("direct_only", false)

	if cls_name.is_empty():
		return {"error": "Class name is required"}

	if not ClassDB.class_exists(cls_name):
		return {"error": "Class not found: %s" % cls_name}

	var inheritors = []
	var all_classes = ClassDB.get_class_list()

	for check_class in all_classes:
		if direct_only:
			# Only check direct parent
			if ClassDB.get_parent_class(check_class) == cls_name:
				inheritors.append(check_class)
		else:
			# Check if class inherits from base at any level
			if check_class != cls_name and ClassDB.is_parent_class(check_class, cls_name):
				inheritors.append(check_class)

	inheritors.sort()

	return {"result": {
		"base_class": cls_name,
		"inheritors": inheritors,
		"count": inheritors.size(),
		"direct_only": direct_only
	}}

func _format_args(args: Array) -> Array:
	var formatted = []
	for arg in args:
		formatted.append({
			"name": arg.get("name", ""),
			"type": _get_type_name(arg.get("type", TYPE_NIL))
		})
	return formatted

func _get_type_name(type_id: int) -> String:
	match type_id:
		TYPE_NIL: return "Nil"
		TYPE_BOOL: return "bool"
		TYPE_INT: return "int"
		TYPE_FLOAT: return "float"
		TYPE_STRING: return "String"
		TYPE_VECTOR2: return "Vector2"
		TYPE_VECTOR2I: return "Vector2i"
		TYPE_RECT2: return "Rect2"
		TYPE_RECT2I: return "Rect2i"
		TYPE_VECTOR3: return "Vector3"
		TYPE_VECTOR3I: return "Vector3i"
		TYPE_TRANSFORM2D: return "Transform2D"
		TYPE_VECTOR4: return "Vector4"
		TYPE_VECTOR4I: return "Vector4i"
		TYPE_PLANE: return "Plane"
		TYPE_QUATERNION: return "Quaternion"
		TYPE_AABB: return "AABB"
		TYPE_BASIS: return "Basis"
		TYPE_TRANSFORM3D: return "Transform3D"
		TYPE_PROJECTION: return "Projection"
		TYPE_COLOR: return "Color"
		TYPE_STRING_NAME: return "StringName"
		TYPE_NODE_PATH: return "NodePath"
		TYPE_RID: return "RID"
		TYPE_OBJECT: return "Object"
		TYPE_CALLABLE: return "Callable"
		TYPE_SIGNAL: return "Signal"
		TYPE_DICTIONARY: return "Dictionary"
		TYPE_ARRAY: return "Array"
		TYPE_PACKED_BYTE_ARRAY: return "PackedByteArray"
		TYPE_PACKED_INT32_ARRAY: return "PackedInt32Array"
		TYPE_PACKED_INT64_ARRAY: return "PackedInt64Array"
		TYPE_PACKED_FLOAT32_ARRAY: return "PackedFloat32Array"
		TYPE_PACKED_FLOAT64_ARRAY: return "PackedFloat64Array"
		TYPE_PACKED_STRING_ARRAY: return "PackedStringArray"
		TYPE_PACKED_VECTOR2_ARRAY: return "PackedVector2Array"
		TYPE_PACKED_VECTOR3_ARRAY: return "PackedVector3Array"
		TYPE_PACKED_COLOR_ARRAY: return "PackedColorArray"
		TYPE_PACKED_VECTOR4_ARRAY: return "PackedVector4Array"
		_: return "Unknown (type_id: %d)" % type_id
