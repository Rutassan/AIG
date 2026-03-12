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
uniform float materialSeparationStrength;
uniform float shadowDepthStrength;
uniform float skyBlendStrength;
uniform float sunScatterStrength;
uniform float ambientLiftStrength;
uniform float hazeStrength;
uniform float materialShadowStrength;

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
    float terrainShadow = mix(1.0 - shadowDepthStrength, 1.0, reliefAccent * 0.66 + sunVisibility * 0.34);
    float reliefLift = mix(0.92, 1.08, reliefAccent);
    float skyBounce = mix(0.92, 1.10 + ambientLiftStrength * 0.08, reliefAccent) * mix(0.88, 1.06 + ambientLiftStrength * 0.05, sunVisibility);
    float ambientLift = mix(0.88, 1.0 + ambientLiftStrength * 0.12, reliefAccent * 0.62 + sunVisibility * 0.38);
    float shadowPresence = clamp((1.0 - sunVisibility) * (0.58 + (1.0 - reliefAccent) * 0.28), 0.0, 1.0);
    float lightMix = baseShade
        * mix(1.0, wrapDiffuse, clamp(shaderStrength * 0.88, 0.0, 1.0))
        * mix(1.0, directShadow, clamp(shaderStrength, 0.0, 1.0))
        * terrainShadow
        * reliefLift
        * skyBounce
        * ambientLift;
    lightMix = clamp(lightMix, 0.24, 1.28);

    float materialBrightness = 1.0
        + grassMask * (0.03 + materialSeparationStrength * 0.03)
        - dirtMask * (0.02 + materialSeparationStrength * 0.02)
        - stoneMask * (0.03 + materialSeparationStrength * 0.03)
        + woodMask * (0.01 + materialSeparationStrength * 0.02)
        + leavesMask * (0.02 + materialSeparationStrength * 0.02);
    float materialWarmth = grassMask * (0.02 + materialSeparationStrength * 0.02)
        + dirtMask * (0.05 + materialSeparationStrength * 0.04)
        + woodMask * (0.07 + materialSeparationStrength * 0.05);
    float materialCoolness = stoneMask * (0.07 + materialSeparationStrength * 0.05)
        + leavesMask * (0.01 + materialSeparationStrength * 0.01);
    vec3 materialTint = vec3(1.0);
    materialTint += grassMask * vec3(-0.02, 0.05, -0.01) * materialSeparationStrength;
    materialTint += dirtMask * vec3(0.04, 0.01, -0.03) * materialSeparationStrength;
    materialTint += stoneMask * vec3(-0.04, -0.01, 0.04) * materialSeparationStrength;
    materialTint += woodMask * vec3(0.06, 0.02, -0.05) * materialSeparationStrength;
    materialTint += leavesMask * vec3(-0.03, 0.04, -0.02) * materialSeparationStrength;
    vec3 shadowMaterialTint = vec3(1.0);
    shadowMaterialTint += grassMask * vec3(-0.03, 0.05, -0.01) * materialShadowStrength;
    shadowMaterialTint += dirtMask * vec3(0.05, 0.00, -0.04) * materialShadowStrength;
    shadowMaterialTint += stoneMask * vec3(-0.05, -0.02, 0.06) * materialShadowStrength;
    shadowMaterialTint += woodMask * vec3(0.07, 0.01, -0.06) * materialShadowStrength;
    shadowMaterialTint += leavesMask * vec3(-0.04, 0.04, -0.02) * materialShadowStrength;

    vec3 viewDir = normalize(cameraPos - fragWorldPos);
    float rim = pow(1.0 - max(dot(viewDir, normal), 0.0), 2.0);
    float sunScatter = pow(max(dot(viewDir, sunDir), 0.0), 11.0) * (0.16 + sunVisibility * 0.26 + reliefAccent * 0.08) * warmLightStrength * (0.88 + sunScatterStrength * 0.55);
    vec3 lit = albedo.rgb * lightMix * materialBrightness;
    lit *= materialTint;
    lit = mix(lit, lit * shadowMaterialTint, shadowPresence * 0.55);
    vec3 shadowTint = mix(vec3(1.0), vec3(0.84, 0.91, 1.06), coolShadowStrength * (1.0 - sunVisibility) * (0.55 + stoneMask * 0.25 + leavesMask * 0.12));
    lit *= shadowTint;
    lit = mix(lit, lit * vec3(1.03, 1.01, 0.97), materialWarmth * warmLightStrength);
    lit = mix(lit, lit * vec3(0.97, 1.00, 1.05), materialCoolness * coolShadowStrength);
    vec3 skyTint = mix(vec3(1.0), vec3(0.84, 0.92, 1.04), skyBlendStrength * (0.32 + reliefAccent * 0.28));
    lit = mix(lit, lit * skyTint, 0.40 + ambientLiftStrength * 0.10);
    lit += vec3(0.024, 0.032, 0.046) * rim * shaderStrength * (0.84 + glowStrength * 0.34 + ambientLiftStrength * 0.18);
    lit += vec3(0.086, 0.066, 0.042) * sunScatter * (0.92 + glowStrength * 0.32);

    float fogStart = min(fogRange.x, fogRange.y);
    float fogEnd = max(fogRange.x, fogRange.y);
    float fogFactor = 0.0;
    if (fogEnd > fogStart)
    {
        fogFactor = clamp((distance(cameraPos, fragWorldPos) - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
        fogFactor = fogFactor * fogFactor * (3.0 - 2.0 * fogFactor);
    }

    float heightFog = clamp((cameraPos.y - fragWorldPos.y) * 0.022, 0.0, 1.0);
    float atmosphereFog = clamp(fogFactor * (0.38 + shaderStrength * 0.52 + ambientLiftStrength * 0.10 + hazeStrength * 0.12) + heightFog * atmosphereStrength * (0.22 + ambientLiftStrength * 0.06 + hazeStrength * 0.04), 0.0, 1.0);
    lit = mix(lit, fogColor.rgb, atmosphereFog);
    lit = mix(lit, mix(lit, fogColor.rgb, 0.16 + hazeStrength * 0.18), fogFactor * hazeStrength * 0.42);

    float luminance = dot(lit, vec3(0.2126, 0.7152, 0.0722));
    vec3 contrasted = vec3(0.5) + (lit - vec3(0.5)) * (1.0 + contrastStrength * 0.18);
    lit = mix(lit, contrasted, clamp(contrastStrength, 0.0, 1.0));
    lit = mix(vec3(luminance), lit, 1.04 + contrastStrength * 0.08 + materialSeparationStrength * 0.06);

    finalColor = vec4(lit, albedo.a);
}
