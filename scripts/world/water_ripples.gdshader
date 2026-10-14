shader_type canvas_item;
render_mode unshaded;

uniform sampler2D water_mask : filter_nearest, repeat_disable;
uniform sampler2D ripples : filter_nearest, repeat_disable;
uniform float elapsed_seconds = 0.0;
varying vec2 map_position;

void vertex() {
    map_position = VERTEX;
}

void fragment() {
    vec4 ground = texture(TEXTURE, UV);
    // 三变体共享同一张 81 组合水 mask；不得把水纹透明度用于通行判断。
    float water = texture(water_mask, vec2(UV.x, fract(UV.y * 3.0))).a;
    // 补丁空间周期与地表三变体分布分离，避免某一变体永远落在空隙。
    vec2 block = floor(map_position / vec2(80.0, 40.0));
    float seed = fract(sin(dot(block, vec2(12.9898, 78.233))) * 43758.5453);
    vec2 local = mod(map_position, vec2(80.0, 40.0)) - vec2(36.0, 12.0);
    float inside = step(0.0, local.x) * step(0.0, local.y)
        * (1.0 - step(24.0, local.x)) * (1.0 - step(24.0, local.y));
    float patch = floor(seed * 4.0);
    vec4 ripple = texture(ripples, vec2((patch * 24.0 + clamp(local.x, 0.0, 23.0)) / 96.0,
        clamp(local.y, 0.0, 23.0) / 24.0));
    float pulse = 0.3 + 0.25 * sin(elapsed_seconds * 1.0471975512 + seed * 6.2831853072);
    COLOR = vec4(mix(ground.rgb, ripple.rgb, ripple.a * inside * water * pulse), ground.a);
}
