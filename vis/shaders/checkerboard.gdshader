shader_type spatial;
render_mode world_vertex_coords;

uniform float size : hint_range(1.0, 100.0) = 25.0;
uniform vec3 color1 : source_color = vec3(1.0, 1.0, 1.0); 
uniform vec3 color2 : source_color = vec3(0.7, 0.7, 0.7);

varying vec3 model_vertex;

void vertex() {
	model_vertex = VERTEX;
	// Called for every vertex the material is visible on.
}

void fragment() {
    vec2 pos = floor(model_vertex.xz / size);
    float pattern_mask = mod(pos.x + mod(pos.y, 2.0), 2.0);
    ALBEDO = mix(color1, color2, pattern_mask);
}