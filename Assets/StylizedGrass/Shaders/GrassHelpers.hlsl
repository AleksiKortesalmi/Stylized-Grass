// Variables for the procedural rendering
// Dynamic
Buffer<float3> _InstancePoints;
Buffer<float4> _Interactors;

// Constant
Buffer<int> _Triangles;
Buffer<float3> _Positions;
Buffer<float3> _Normals;
Buffer<float2> _UVs;

uniform uint _StartIndex;
uniform uint _BaseVertexIndex;
uniform uint _NumInteractors;
uniform float4x4 _ObjectToWorld;
uniform float _WindStrength;
uniform float _WindSpeed;
uniform float3 _WindDirection;
uniform float _NoiseScale;
uniform int _RampTexWidth;
uniform float3 _Scale;
uniform float2 _YScaleRange;
uniform float2 _RotRange;
uniform float _GeometryNoiseScale;
uniform float _InteractorStrength;

float3 MaxVector3(float value, float3 value3)
{
    value3.r = max(value, value3.r);
    value3.g = max(value, value3.g);
    value3.b = max(value, value3.b);
    
    return value3;
}

float GetColorIntensity(float3 color)
{
    float intensity = max(color.r, color.g);
    intensity = max(intensity, color.b);
    
    return intensity;
}

float4 SampleRamp(float light, sampler2D rampTexture)
{
    // Make the sample position to the middle of pixels
    light = (floor(light * _RampTexWidth) + 0.5f) / _RampTexWidth;
    // Sample the ramp texture (should be "1 dimensional", for example 4x1 resolution upscaled resulting in 4 light steps)
    return tex2D(rampTexture, float2(min(light, .99f), 0));
}

float3 CalculateMainLight(float4 posWS, half3 normal, float4 shadowColor, sampler2D rampTexture)
{
    float4 shadowCoord = TransformWorldToShadowCoord(posWS.xyz);
    Light mainLight = GetMainLight(shadowCoord);
    
    float3 color = (dot(mainLight.direction, normal) + 1) * .5;
    // Ramp the main light
    color = SampleRamp(color.x, rampTexture).rgb * mainLight.color;

    // Apply shadow
    return color * lerp(shadowColor, float4(1, 1, 1, 1), mainLight.shadowAttenuation).rgb;
}

// Simplified main light for billboard grass 
// (mainly to just blend with light color and receive shadows)
float3 CalculateSimpleMainLight(float4 shadowColor, float4 posWS)
{
    float4 shadowCoord = TransformWorldToShadowCoord(posWS.xyz);
    // GetMainLight in RealtimeLights.hlsl of URP shader library
    Light mainLight = GetMainLight(shadowCoord);

    // Apply shadow
    return mainLight.color * lerp(shadowColor, float4(1, 1, 1, 1), mainLight.shadowAttenuation).rgb;
}

// Additional lights at the same time capped to 3
// Wrote inline to fasten compiling (slow was caused by looped tex2D call?)
float3 CalculateAdditionalLight(float4 posWS, float4 shadowColor, sampler2D rampTexture)
{
    float3 totalLight = 0;
    uint lightsCount = GetAdditionalLightsCount();
    
    for (uint i = 0; i < lightsCount; i++)
    {
        Light light = GetAdditionalLight(i, posWS.xyz);
        
        // Get square root of the distance attenuation to get more linear light falloff
        float3 distAttenuatedColor = light.color * sqrt(light.distanceAttenuation);
        
        half3 shadow = half3(AdditionalLightRealtimeShadow(i, posWS.xyz, light.direction), 1, 1);
        shadow = lerp(shadowColor.rgb, half3(1, 1, 1), shadow.r);
        
        // Ramp the light
        totalLight = totalLight + MaxVector3(0, normalize(distAttenuatedColor) * SampleRamp(GetColorIntensity(distAttenuatedColor), rampTexture).rgb * shadow).rgb * GetColorIntensity(light.color);
    }
    
    // Clamp to 0
    return totalLight;
}

// Transforms posOS and forward
void RandomizeInstance(inout float3 posOS, inout float3 forward, uint instanceID)
{
#if RANDOM_HEIGHT_ON || RANDOM_ROTATION_ON
    // Randomized Y scale
    // Get random 0-1
    float rand01 = snoise(_InstancePoints[instanceID].xz * _GeometryNoiseScale) * .5f + .5f;
#endif

#if RANDOM_HEIGHT_ON
    // Lerp that to get height
    float randScale = lerp(_YScaleRange.x, _YScaleRange.y, rand01);
    
    float4x4 scaleMatrix =
    {
        1, 0, 0, 0,
        0, randScale, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1
    };
    
    posOS = mul(scaleMatrix, float4(posOS, 1.0f)).xyz;
#endif

#if RANDOM_ROTATION_ON
    // Random rotation in the y axis
    float randAngle = lerp(_RotRange.x, _RotRange.y, rand01) / 55; // Dont know about the 55 it just works

    float4x4 rotateMatrix =
    {
        cos(randAngle), 0, sin(randAngle), 0,
        0, 1, 0, 0,
        -sin(randAngle), 0, cos(randAngle), 0,
        0, 0, 0, 1
    };
    
    forward = mul(rotateMatrix, float4(forward, 1.0f)).xyz;
#endif
}

void ScaleInstance(inout float3 posOS)
{
    posOS *= _Scale;
}

// Returns direction towards camera limited in the Y axis
float3 CalculateBillboardForward(uint instanceID)
{
    // Billboarding
    float3 worldInstancePoint = mul(_ObjectToWorld, float4(_InstancePoints[instanceID], 1)).xyz;
    float dist = distance(_WorldSpaceCameraPos, worldInstancePoint);

    // Look at vector
    float3 forward = _WorldSpaceCameraPos - worldInstancePoint;
    // Limit Y to 1 to prevent grass rotating too far in the X axis (when applied with LookAt matrix)
    forward.y = min(forward.y, 0.4f);
    return forward = normalize(forward);
}

// Calculate forward for interactor effect
// Returns true if in range of interactor + smoothing distance
bool CalculateInteractorForward(uint instanceID, float3 posOS, out float3 forward, out float blend)
{
    for (uint i = 0; i < _NumInteractors; i++)
    {
        float3 worldInstancePoint = mul(_ObjectToWorld, float4(_InstancePoints[instanceID], 1)).xyz;
        float interactorRadius = _Interactors[i].w;
        float dist = distance(_Interactors[i].xyz, worldInstancePoint);

        float rotationSmoothingDistance = .5f;
        // If distance larger than interactor radius + rotation smoothing => skip
        if (dist > interactorRadius + rotationSmoothingDistance)
        {
            continue;
        }
        
        // Calculate smoothing outside interactor range
        // More specifically, calculate forward towards interactor by linearly interpolating between original forward and look at vector to interactor with distance to interactor radius 
        forward = lerp(float3(0, 0, 1), 
            _Interactors[i].xyz - worldInstancePoint, 
            // Smoothing factor
            // In range of interactor = 1, Out of range by 0.25 units = 0.5
            (rotationSmoothingDistance - max(dist - interactorRadius, 0)) / rotationSmoothingDistance);
        
        // Adjust the bending of the grass by multiplying the lookat vectors Y component
        // Use distance to center as a strength multiplier, which is done by interpolating between original forward and lookat vector
        float strengthByDistance = max(interactorRadius - dist, 0) / interactorRadius;
        forward.y *= strengthByDistance * _InteractorStrength;
        
        // Dividing by height ensures that the vertices on ground dont lift up from all the bending
        forward.y *= posOS.y / clamp(posOS.y, 0.001f, 1);
        forward = normalize(forward);

        blend = strengthByDistance / max(strengthByDistance, 0.2f);

        // Support only one interactor per instance
        return true;
    }

    return false;
}

// Applies LookAt matrix transformation
float3 LookAtTransformation(inout float3 posOS, inout float3 normal, float3 forward)
{
    // Calculate rest of the vectors for the coordinate system of the LookAt vector
    // Use temporary up to calculate right for the LookAt coordinate system (needed for cross product)
    float3 right = normalize(cross(forward, float3(0, -1, 0)));
    float3 up = normalize(cross(forward, right));

    // Put together the transformation matrix of the new coordinate system that happens to point straight towards our given "forward" direction
    float4x4 lookAt =
    {
        right.x, up.x, forward.x, 0,
        right.y, up.y, forward.y, 0,
        right.z, up.z, forward.z, 0,
        0, 0, 0, 1
    };

    // Transform both object space position and normal
    posOS = mul(lookAt, float4(posOS, 1)).xyz;
    normal = mul(lookAt, float4(normal, 1.0f)).xyz;

    return normal;
}

float3 CalculateWind(float3 pos, uint instanceID)
{
    // 3D Simplex randomize sampled based on position (simulates wind a lil bit)
    float2 offset = float2(0, 0);
    uint octaveCount = 2;
    for (uint octave = 1; octave < octaveCount + 1; octave++)
    {
        offset += (snoise(_InstancePoints[instanceID].xyz * _NoiseScale / (octave * octave) + _WindDirection * _Time.y * _WindSpeed) * -.5 - .5) * ((octave * -1 + octaveCount + 1) / octaveCount) * _WindDirection.xz;
    }
    offset /= octaveCount;
    offset *= _WindStrength;
    // Exponential curve on height
    offset *= pos.y * pos.y;
    
    pos.xz = pos.xz + offset;
    
    return pos;
}