shader_type spatial;
render_mode cull_back;

uniform vec3 color : source_color = vec3(1, 1, 1);

varying vec3 model_vertex;

void vertex() {
	model_vertex = VERTEX;
	// Called for every vertex the material is visible on.
}

void fragment() {
	if (model_vertex.y < -1.0)
	{
		discard;
	}

	NORMAL = normalize(cross(dFdy(VERTEX), dFdx(VERTEX)));
	ALBEDO = color;
	// Called for every pixel the material is visible on.
}

//void light() {
//	// Called for every pixel for every light affecting the material.
//	// Uncomment to replace the default light processing function with this one.
//}
