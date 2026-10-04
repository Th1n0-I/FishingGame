#ifndef CLOUD_RAIN_INCLUDED
#define CLOUD_RAIN_INCLUDED

// Storm cell outlines are bent by moving the storm map lookup at p (world xz minus the wind travel) by up to 1.25 km.
// The noise is the weather map's b and a channels at p / STORM_WARP_SCALE and mip STORM_WARP_MIP: smooth enough that
// the bend never folds over (at mip 0 it did over a third of the map).
static const float STORM_WARP_SCALE = 24000.0;
static const float STORM_WARP_MIP = 5.0;

float2 storm_warp(float2 p, float2 noise)
{
    return p + (noise - 0.5) * 2500.0;
}

// How much rain falls at p, without the strands. Under the active storm cells it falls as a column narrower than the
// cell, between them light rain under the thickest cover, in patches and less of it when the storms take over. Shared
// by the curtains (VolumetricCompute, veil_strength 0.15, more and the light rain adds up to a grey wall at the
// horizon) and the streaks around the camera (the composite, 1), so they agree on where it rains.
// weather: the weather map at p / 128000 (r = coverage noise, low is cloudy), cell: the storm map at the bent uv
// (r = falloff, g = the cell's random value), patches: the weather map's a at p / 24000 + 0.5, rain: x rain amount,
// y coverage, z towers, w share of active storm cells. in_cell: whether p is under an active cell at all.
float rain_amount(float4 weather, float2 cell, float patches, float4 rain, float veil_strength, out bool in_cell)
{
    float active = saturate((rain.w - cell.y) * 8.0);
    in_cell = cell.x * active > 0.001;
    float shaft = smoothstep(0.25, 0.75, cell.x) * active;
    float veil = saturate((rain.y - weather.r - 0.2) * 3.0) * (1.0 - 0.8 * rain.z) * veil_strength *
        smoothstep(0.45, 0.75, patches);
    return rain.x * max(shaft, veil);
}

#endif
