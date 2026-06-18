#ifndef GERSTNER_INCLUDED
#define GERSTNER_INCLUDED

#define WAVE_COUNT 40

static const float gravity = 9.81f;
static const float kx[WAVE_COUNT] = {
        0.0470, 0.0646, 0.0431, 0.0760, 0.0526, 0.0700, 0.0389, 0.0631, 0.0762, 0.0505,
        0.0545, 0.0745, 0.0392, 0.0685, 0.0478, 0.0816, 0.0502, 0.0716, 0.0402, 0.0653,
        0.1119, 0.1835, 0.0958, 0.2102, 0.1172, 0.1964, 0.0862, 0.1733, 0.1616, 0.1368,
        0.1112, 0.2040, 0.0830, 0.1910, 0.0913, 0.2177, 0.0931, 0.1812, 0.0741, 0.1748
};
static const float kz[WAVE_COUNT] = {
    0.0223, 0.0295, 0.0211, 0.0334, 0.0267, 0.0296, 0.0205, 0.0253, 0.0420, 0.0192,
     0.0317, 0.0263, 0.0240, 0.0223, 0.0310, 0.0239, 0.0347, 0.0188, 0.0297, 0.0152,
     0.0875, 0.0362, 0.0787, 0.0344, 0.1030, 0.0253, 0.0821, 0.0162, 0.1651, 0.0075,
     0.1192, 0.0039, 0.0975,-0.0036, 0.1165,-0.0122, 0.1289,-0.0167, 0.1105,-0.0207
 };
static const float amp[WAVE_COUNT] = {
    0.48, 0.44, 0.51, 0.43, 0.47, 0.45, 0.50, 0.42, 0.49, 0.46,
   0.52, 0.41, 0.48, 0.44, 0.47, 0.43, 0.50, 0.45, 0.49, 0.46,
   0.19, 0.17, 0.20, 0.16, 0.19, 0.18, 0.21, 0.17, 0.20, 0.18,
   0.22, 0.16, 0.19, 0.17, 0.20, 0.18, 0.21, 0.17, 0.20, 0.19
};
static const float p[WAVE_COUNT] = {
    0.0, 2.4, 1.1, 4.3, 5.7, 3.2, 0.9, 3.7, 1.5, 4.9,
    2.1, 5.3, 0.6, 3.9, 1.8, 4.5, 2.7, 5.9, 0.3, 3.4,
    0.7, 3.6, 5.1, 1.9, 2.8, 4.7, 0.4, 3.1, 5.5, 1.3,
    4.1, 2.2, 5.8, 0.8, 3.3, 1.6, 4.4, 2.9, 5.2, 0.5
};
void Gerstner_float(float3 inPos, float t, float intensity, out float3 outPos, out float3 outNormal) {
    
    outPos = float3(0,0,0);
    outNormal = float3(0,0,0);
    float3 tangent = float3(1,0,0);
    float3 bitangent = float3(0,0,1);
    
    float steepness = intensity * 0.6;
    float heightMultiplier = intensity * 0.4;
    
    for (int i = 0; i < WAVE_COUNT; i++)
    {
        
        float k = sqrt(pow(kx[i],2) + pow(kz[i],2));
        float angularFrequency = sqrt(gravity * k);
        float angle = kx[i]*inPos.x+kz[i]*inPos.z-angularFrequency*t-p[i];
        
        float sinAngle = sin(angle);
        float cosAngle = cos(angle);
        
        outPos.x += (kx[i]/k)*(amp[i])*sinAngle*steepness;
        outPos.z += (kz[i]/k)*(amp[i])*sinAngle*steepness;
        outPos.y += amp[i]*cosAngle*heightMultiplier;
        
        tangent.x += -(kx[i]/k)*amp[i]*kx[i]*cosAngle*steepness;
        tangent.z += -(kz[i]/k)*amp[i]*kx[i]*cosAngle*steepness;
        tangent.y += amp[i]*kx[i]*(-sinAngle)*heightMultiplier;
        
        bitangent.x += -(kx[i]/k)*amp[i]*kz[i]*cosAngle*steepness;
        bitangent.z += -(kz[i]/k)*amp[i]*kz[i]*cosAngle*steepness;
        bitangent.y += amp[i]*kz[i]*(-sinAngle)*heightMultiplier;
    }
    
    outPos.x = inPos.x - outPos.x;
    outPos.z = inPos.z - outPos.z;
    outPos.y = inPos.y + outPos.y;
    
    outNormal = normalize(cross(bitangent,tangent));
}

#endif // GERSTNER_INCLUDED