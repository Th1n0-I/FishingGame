#ifndef CLOUD_PROCEDURAL_SKY_INCLUDED
#define CLOUD_PROCEDURAL_SKY_INCLUDED

// The colour of Unity's built-in Skybox/Procedural (the sky branch of its vertex program, Unity built-in shader source,
// MIT license), so far clouds and far ground can fade into exactly the colour the sky has behind them.
// NoiseController copies the skybox material's values in here every frame.

// x: exposure, y: atmosphere thickness.
float4 _CloudSkyParams;
// rgb: sky tint in gamma space, like the skybox shader uses it.
float4 _CloudSkyTint;
// xyz: direction to the sun.
float4 _CloudSkySunDir;

float cloud_sky_scale(float in_cos)
{
    float x = 1.0 - in_cos;
    return 0.25 * exp(-0.00287 + x * (0.459 + x * (3.83 + x * (-6.80 + x * 5.25))));
}

// Linear sky colour in a direction. Below the horizon it gives the horizon colour, that is what the haze towards the
// ground and towards clouds seen from above looks like.
float3 procedural_sky(float3 ray)
{
    float3 eye_ray = normalize(float3(ray.x, max(ray.y, 0.0), ray.z) + float3(0.0, 1e-4, 0.0));

    const float outer_radius = 1.025;
    const float outer_radius2 = outer_radius * outer_radius;
    const float inner_radius = 1.0;
    const float camera_height = 0.0001;
    const float scale = 1.0 / (outer_radius - 1.0);
    const float scale_depth = 0.25;
    const float scale_over_scale_depth = scale / scale_depth;
    const float mie = 0.0010;
    const float sun_brightness = 20.0;
    const float pi = 3.14159265;

    float rayleigh = lerp(0.0, 0.0025, pow(max(_CloudSkyParams.y, 0.0), 2.5));
    float3 wavelength = lerp(float3(0.65, 0.57, 0.475) - 0.15, float3(0.65, 0.57, 0.475) + 0.15, 1.0 - _CloudSkyTint.rgb);
    float3 inv_wavelength = 1.0 / pow(wavelength, 4.0);
    float3 sun_dir = _CloudSkySunDir.xyz;

    float3 camera_pos = float3(0.0, inner_radius + camera_height, 0.0);
    float far = sqrt(outer_radius2 + eye_ray.y * eye_ray.y - 1.0) - eye_ray.y;
    float start_offset = exp(scale_over_scale_depth * -camera_height) * cloud_sky_scale(eye_ray.y);

    float sample_length = far * 0.5;
    float scaled_length = sample_length * scale;
    float3 sample_ray = eye_ray * sample_length;
    float3 sample_point = camera_pos + sample_ray * 0.5;
    float3 front_color = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < 2; i++)
    {
        float height = length(sample_point);
        float depth = exp(scale_over_scale_depth * (inner_radius - height));
        float light_angle = dot(sun_dir, sample_point) / height;
        float camera_angle = dot(eye_ray, sample_point) / height;
        float scatter = start_offset + depth * (cloud_sky_scale(light_angle) - cloud_sky_scale(camera_angle));
        float3 attenuate = exp(-clamp(scatter, 0.0, 50.0) * (inv_wavelength * rayleigh * 4.0 * pi + mie * 4.0 * pi));
        front_color += attenuate * (depth * scaled_length);
        sample_point += sample_ray;
    }

    float eye_cos = dot(sun_dir, eye_ray);
    return _CloudSkyParams.x * front_color * (inv_wavelength * rayleigh * sun_brightness) * (0.75 + 0.75 * eye_cos * eye_cos);
}

#endif
