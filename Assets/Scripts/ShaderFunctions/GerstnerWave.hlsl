#ifndef GERSTNER_INCLUDED
#define GERSTNER_INCLUDED

#define WAVE_COUNT 8

static const float gravity = 9.81f;
static const float kx[WAVE_COUNT]    = { 0.08, 0.14, -0.10,  0.22, -0.26, 0.34,  0.40, -0.45 };
static const float kz[WAVE_COUNT]    = { 0.03, 0.20,  0.18, -0.12,  0.16, 0.28, -0.22,  0.30 };
static const float a[WAVE_COUNT]     = { 0.40, 0.21,  0.15,  0.09,  0.07, 0.04,  0.03,  0.02 };
static const float p[WAVE_COUNT]     = { 0.00, 1.30,  2.10,  0.50,  3.00, 1.80,  4.10,  2.60 };
void Gerstner_float(float3 inPos, float t, out float3 outPos) {
    outPos = float3(0,0,0);
    
    for (int i = 0; i < WAVE_COUNT; i++)
    {
        float k = sqrt(pow(kx[i],2) + pow(kz[i],2));
        float angularFrequency = sqrt(gravity * k);
        float angle = kx[i] * inPos.x + kz[i] * inPos.z - angularFrequency * t - p[i];
        
        outPos.x += (kx[i]/k)*(a[i])*sin(angle);
        outPos.z += (kz[i]/k)*(a[i])*sin(angle);
        outPos.y += a[i]*cos(angle);
    }
    
    outPos.x = inPos.x - outPos.x;
    outPos.z = inPos.z - outPos.z;
    outPos.y = inPos.y - outPos.y;
}

void GerstnerNormal_float(float3 inPos, float t, out float3 normal)
{
    normal = float3(0,0,0);
    float3 tangent = float3(1,0,0);
    float3 bitangent = float3(0,0,1);
    
    for (int i = 0; i < WAVE_COUNT; i++)
    {
        float k = sqrt(pow(kx[i],2) + pow(kz[i],2));
        float angularFrequency = sqrt(gravity * k);
        float angle = kx[i] * inPos.x + kz[i] * inPos.z - angularFrequency * t - p[i];
        
        tangent += float3(-(kx[i]/k)*a[i]*kx[i]*cos(angle),a[i]*kx[i]*(-sin(angle)),-(kz[i]/k)*a[i]*kx[i]*cos(angle));
        bitangent += float3(-(kx[i]/k)*a[i]*kz[i]*cos(angle),a[i]*kz[i]*(-sin(angle)),-(kz[i]/k)*a[i]*kz[i]*cos(angle));
    }
    normal = normalize(cross(bitangent,tangent)); 
}

#endif // GERSTNER_INCLUDED