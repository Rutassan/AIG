#version 330

in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragNormal;
in vec3 fragWorldPos;

out vec4 finalColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec3 cameraPos;
uniform vec3 sunDirection;
uniform vec4 fogColor;
uniform vec2 fogRange;
uniform float shaderStrength;
uniform float shadowStrength;
uniform float atmosphereStrength;
uniform float warmLightStrength;

void main()
{
    vec4 texel = texture(texture0, fragTexCoord);
    vec4 albedo = vec4(texel.rgb * colDiffuse.rgb, texel.a * colDiffuse.a);
    if (albedo.a <= 0.01)
    {
        discard;
    }

    vec3 normal = normalize(fragNormal);
    vec3 sunDir = normalize(-sunDirection);
    float diffuse = max(dot(normal, sunDir), 0.0);
    float wrapDiffuse = diffuse * 0.72 + 0.28;
    float baseShade = clamp(fragColor.r, 0.0, 1.0);
    float sunVisibility = clamp(fragColor.g, 0.0, 1.0);
    float reliefAccent = clamp(fragColor.b, 0.0, 1.0);
    float directShadow = mix(1.0 - shadowStrength, 1.0, sunVisibility);
    float reliefLift = mix(0.92, 1.08, reliefAccent);
    float skyBounce = mix(0.94, 1.08, reliefAccent) * mix(0.90, 1.05, sunVisibility);
    float lightMix = baseShade
        * mix(1.0, wrapDiffuse, clamp(shaderStrength * 0.88, 0.0, 1.0))
        * mix(1.0, directShadow, clamp(shaderStrength, 0.0, 1.0))
        * reliefLift
        * skyBounce;
    lightMix = clamp(lightMix, 0.24, 1.28);

    vec3 viewDir = normalize(cameraPos - fragWorldPos);
    float rim = pow(1.0 - max(dot(viewDir, normal), 0.0), 2.0);
    float sunScatter = pow(max(dot(viewDir, sunDir), 0.0), 12.0) * (0.18 + sunVisibility * 0.22) * warmLightStrength;
    vec3 lit = albedo.rgb * lightMix;
    lit += vec3(0.028, 0.034, 0.045) * rim * shaderStrength;
    lit += vec3(0.074, 0.058, 0.038) * sunScatter;

    float fogStart = min(fogRange.x, fogRange.y);
    float fogEnd = max(fogRange.x, fogRange.y);
    float fogFactor = 0.0;
    if (fogEnd > fogStart)
    {
        fogFactor = clamp((distance(cameraPos, fragWorldPos) - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
        fogFactor = fogFactor * fogFactor * (3.0 - 2.0 * fogFactor);
    }

    float heightFog = clamp((cameraPos.y - fragWorldPos.y) * 0.022, 0.0, 1.0);
    float atmosphereFog = clamp(fogFactor * (0.38 + shaderStrength * 0.52) + heightFog * atmosphereStrength * 0.24, 0.0, 1.0);
    lit = mix(lit, fogColor.rgb, atmosphereFog);
    finalColor = vec4(lit, albedo.a);
}
