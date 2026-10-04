#ifndef CLOUD_RAIN_INCLUDED
#define CLOUD_RAIN_INCLUDED

// How hard it rains under a point of the weather map, 0 to 1, for the rain around the camera in the composite. The rain
// curtains in VolumetricCompute (rain_density) shape the same values into shafts under the storm cells.
// weather: the weather map sample (r = coverage noise, low is cloudy), storm_cell: the storm map sample
// (r = falloff, g = the cell's random value), rain: x rain amount, y coverage, z towers, w share of active storm cells.
float cloud_precipitation(float4 weather, float2 storm_cell, float4 rain)
{
    // Under the thickest part of the cloud cover...
    float dense = saturate((rain.y - weather.r - 0.2) * 3.0);
    // ...and hard under the active storm cells. With storms about, most of the rain comes from the cells.
    float cell = storm_cell.x * saturate((rain.w - storm_cell.y) * 8.0);
    return rain.x * max(dense * (1.0 - 0.8 * rain.z), cell);
}

// Bends the storm cell outlines: moves the storm map lookup at p (world xz minus the wind travel) by up to 2.5 km.
// noise: the weather map's b and a channels (smooth cirrus noise) sampled at p / 24000.
float2 storm_warp(float2 p, float2 noise)
{
    return p + (noise - 0.5) * 5000.0;
}

#endif
