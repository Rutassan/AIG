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
uniform float coolShadowStrength;
uniform float contrastStrength;
uniform float glowStrength;

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
    float materialCode = fragColor.a * 255.0;

    float grassMask = 1.0 - step(52.0, abs(materialCode - 32.0));
    float dirtMask = 1.0 - step(52.0, abs(materialCode - 72.0));
    float stoneMask = 1.0 - step(52.0, abs(materialCode - 128.0));
    float woodMask = 1.0 - step(52.0, abs(materialCode - 184.0));
    float leavesMask = 1.0 - step(52.0, abs(materialCode - 232.0));

    float directShadow = mix(1.0 - shadowStrength, 1.0, sunVisibility);
    float reliefLift = mix(0.92, 1.08, reliefAccent);
    float skyBounce = mix(0.94, 1.08, reliefAccent) * mix(0.90, 1.05, sunVisibility);
    float lightMix = baseShade
        * mix(1.0, wrapDiffuse, clamp(shaderStrength * 0.88, 0.0, 1.0))
        * mix(1.0, directShadow, clamp(shaderStrength, 0.0, 1.0))
        * reliefLift
        * skyBounce;
    lightMix = clamp(lightMix, 0.24, 1.28);

    float materialBrightness = 1.0
        + grassMask * 0.03
        - dirtMask * 0.02
        - stoneMask * 0.03
        + woodMask * 0.01
        + leavesMask * 0.02;
    float materialWarmth = grassMask * 0.02 + dirtMask * 0.05 + woodMask * 0.07;
    float materialCoolness = stoneMask * 0.07 + leavesMask * 0.01;

    vec3 viewDir = normalize(cameraPos - fragWorldPos);
    float rim = pow(1.0 - max(dot(viewDir, normal), 0.0), 2.0);
    float sunScatter = pow(max(dot(viewDir, sunDir), 0.0), 12.0) * (0.18 + sunVisibility * 0.22) * warmLightStrength;
    vec3 lit = albedo.rgb * lightMix * materialBrightness;
    vec3 shadowTint = mix(vec3(1.0), vec3(0.84, 0.91, 1.06), coolShadowStrength * (1.0 - sunVisibility) * (0.55 + stoneMask * 0.25 + leavesMask * 0.12));
    lit *= shadowTint;
    lit = mix(lit, lit * vec3(1.03, 1.01, 0.97), materialWarmth * warmLightStrength);
    lit = mix(lit, lit * vec3(0.97, 1.00, 1.05), materialCoolness * coolShadowStrength);
    lit += vec3(0.028, 0.034, 0.045) * rim * shaderStrength * (0.85 + glowStrength * 0.35);
    lit += vec3(0.074, 0.058, 0.038) * sunScatter * (0.9 + glowStrength * 0.3);

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

    float luminance = dot(lit, vec3(0.2126, 0.7152, 0.0722));
    vec3 contrasted = vec3(0.5) + (lit - vec3(0.5)) * (1.0 + contrastStrength * 0.18);
    lit = mix(lit, contrasted, clamp(contrastStrength, 0.0, 1.0));
    lit = mix(vec3(luminance), lit, 1.04 + contrastStrength * 0.08);

    finalColor = vec4(lit, albedo.a);
}
