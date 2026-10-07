shader_type canvas_item;
// 清亮 v2 rooted_wind：主游戏提供视觉时间；固定根部与透明 padding 不形变。
uniform float visual_time = 0.0;
uniform float root_y_px = 1.0;
uniform float fixed_base_px = 1.0;
uniform float amplitude_px = 0.0;
uniform float phase = 0.0;
varying vec4 vertex_tint;
void vertex() { vertex_tint = COLOR; }
void fragment() {
	float y_px = UV.y / TEXTURE_PIXEL_SIZE.y - 0.5;
	float weight = clamp((root_y_px - y_px - fixed_base_px) / max(root_y_px - fixed_base_px, 1.0), 0.0, 1.0);
	float sway = sin(visual_time * 1.75 + phase) * 0.72 + sin(visual_time * 0.7 + phase * 0.73) * 0.28;
	float shift_px = round(sway * amplitude_px * pow(weight, 1.65));
	vec2 sample_uv = UV - vec2(shift_px * TEXTURE_PIXEL_SIZE.x, 0.0);
	COLOR = sample_uv.x < 0.0 || sample_uv.x > 1.0 ? vec4(0.0) : vertex_tint * texture(TEXTURE, sample_uv);
}
