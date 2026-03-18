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
uniform float horizonDepthStrength;
uniform float foliageTranslucencyStrength;
uniform float secondaryBounceStrength;
uniform float distanceMaterialStrength;
uniform float skyResponseStrength;
uniform float farGradientStrength;
uniform float shadowContourStrength;
uniform float atmosphereGradientStrength;
uniform float distanceShadowLiftStrength;
uniform float skyContourStrength;
uniform float distantSilhouetteStrength;
uniform float atmosphericContourStrength;
uniform float reliefBridgeStrength;
uniform float shadowHazeFusionStrength;
uniform float lightPlasticityStrength;
uniform float farReadabilityStrength;
uniform float finalCohesionStrength;
uniform float viewMaterialStrength;
uniform float shadowCascadeBlendStrength;
uniform float farWorldCohesionStrength;
uniform sampler2D shadowMapNear;
uniform sampler2D shadowMapFar;
uniform float shadowMapEnabled;
uniform vec3 shadowNearOrigin;
uniform vec3 shadowNearRight;
uniform vec3 shadowNearUp;
uniform vec3 shadowNearForward;
uniform vec3 shadowNearHalfExtents;
uniform vec3 shadowFarOrigin;
uniform vec3 shadowFarRight;
uniform vec3 shadowFarUp;
uniform vec3 shadowFarForward;
uniform vec3 shadowFarHalfExtents;
uniform vec2 shadowDistanceRange;
uniform vec2 shadowFarProxyRange;
uniform float shadowFarProxyStrength;
uniform vec2 shadowFilterRadius;
uniform float shadowCascadeBlendWidth;
uniform float shadowMapBias;
uniform float shadowSlopeBiasStrength;
uniform float shadowMapStrength;

float sampleShadowMap(
    sampler2D shadowMap,
    vec3 worldPos,
    vec3 origin,
    vec3 rightAxis,
    vec3 upAxis,
    vec3 forwardAxis,
    vec3 halfExtents,
    float bias,
    float filterRadius)
{
    vec3 relative = worldPos - origin;
    float localX = dot(relative, rightAxis);
    float localY = dot(relative, upAxis);
    float localZ = dot(relative, forwardAxis);

    if (abs(localX) > halfExtents.x || abs(localY) > halfExtents.y || abs(localZ) > halfExtents.z)
    {
        return 1.0;
    }

    vec2 uv = vec2(
        localX / (halfExtents.x * 2.0) + 0.5,
        localY / (halfExtents.y * 2.0) + 0.5);
    float receiverDepth = localZ / (halfExtents.z * 2.0) + 0.5;
    float compareBias = bias + 0.0035;
    int radius = int(clamp(floor(filterRadius + 0.5), 0.0, 2.0));
    if (radius <= 0)
    {
        float storedDepth = texture(shadowMap, uv).r;
        return 1.0 - smoothstep(storedDepth + compareBias - 0.012, storedDepth + compareBias + 0.012, receiverDepth);
    }

    vec2 texelSize = 1.0 / vec2(textureSize(shadowMap, 0));
    float visibility = 0.0;
    float taps = 0.0;
    for (int oy = -2; oy <= 2; oy++)
    {
        if (abs(oy) > radius)
        {
            continue;
        }

        for (int ox = -2; ox <= 2; ox++)
        {
            if (abs(ox) > radius)
            {
                continue;
            }

            float storedDepth = texture(shadowMap, uv + vec2(ox, oy) * texelSize).r;
            visibility += 1.0 - smoothstep(storedDepth + compareBias - 0.012, storedDepth + compareBias + 0.012, receiverDepth);
            taps += 1.0;
        }
    }

    return taps > 0.0 ? visibility / taps : 1.0;
}

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
    float foliageMask = clamp(grassMask * 0.25 + leavesMask, 0.0, 1.0);

    float sourcePresence = clamp(max(baseShade, sunVisibility), 0.0, 1.0);
    float directShadow = mix(1.0 - shadowStrength, 1.0, sunVisibility);
    float terrainShadow = mix(1.0 - shadowDepthStrength, 1.0, reliefAccent * 0.66 + sunVisibility * 0.34);
    float reliefLift = mix(0.88, 1.06, reliefAccent);
    float skyBounce = mix(0.76, 1.08 + ambientLiftStrength * 0.06, reliefAccent) * mix(0.60, 1.02 + ambientLiftStrength * 0.04, sourcePresence);
    float ambientLift = mix(0.72, 1.0 + ambientLiftStrength * 0.08, reliefAccent * 0.40 + sourcePresence * 0.60);
    float shadowPresence = clamp((1.0 - sunVisibility) * (0.58 + (1.0 - reliefAccent) * 0.28), 0.0, 1.0);
    float horizonDepth = clamp(horizonDepthStrength * (1.0 - sunVisibility) * (0.32 + (1.0 - reliefAccent) * 0.44), 0.0, 1.0);
    float reliefBridge = clamp((reliefAccent * 0.34 + sunVisibility * 0.16 + (1.0 - baseShade) * 0.12) * reliefBridgeStrength, 0.0, 0.32);
    float shadowContour = clamp((1.0 - sunVisibility) * (0.34 + reliefAccent * 0.22 + (1.0 - baseShade) * 0.20) * shadowContourStrength, 0.0, 0.34);
    float distanceShadowLift = clamp((1.0 - sunVisibility) * (0.18 + horizonDepth * 0.42 + (1.0 - reliefAccent) * 0.18) * distanceShadowLiftStrength, 0.0, 0.28);
    float distantSilhouette = clamp((1.0 - sunVisibility) * (0.12 + (1.0 - reliefAccent) * 0.22 + (1.0 - baseShade) * 0.18) * distantSilhouetteStrength, 0.0, 0.24);
    float lightPlasticity = clamp((reliefAccent * 0.28 + sunVisibility * 0.16 + diffuse * 0.18 + (1.0 - baseShade) * 0.10) * lightPlasticityStrength, 0.0, 0.26);
    float worldDistance = distance(cameraPos, fragWorldPos);
    float shadowMapMix = clamp(shadowMapEnabled, 0.0, 1.0);
    float slopeBias = shadowMapBias + (1.0 - diffuse) * shadowSlopeBiasStrength;
    float shadowNearVisibility = sampleShadowMap(
        shadowMapNear,
        fragWorldPos,
        shadowNearOrigin,
        shadowNearRight,
        shadowNearUp,
        shadowNearForward,
        shadowNearHalfExtents,
        slopeBias,
        shadowFilterRadius.x);
    float shadowFarVisibility = sampleShadowMap(
        shadowMapFar,
        fragWorldPos,
        shadowFarOrigin,
        shadowFarRight,
        shadowFarUp,
        shadowFarForward,
        shadowFarHalfExtents,
        slopeBias * 1.15,
        shadowFilterRadius.y);
    float farShadowProxy = clamp(smoothstep(shadowFarProxyRange.x, shadowFarProxyRange.y, worldDistance), 0.0, 1.0);
    float cascadeBlend = clamp(0.08 + shadowCascadeBlendStrength * 0.24 + shadowCascadeBlendWidth * 0.60, 0.08, 0.44);
    float nearBlend = 1.0 - smoothstep(
        shadowDistanceRange.x * (0.78 - cascadeBlend * 0.20),
        shadowDistanceRange.x * (1.00 + cascadeBlend * 0.14),
        worldDistance);
    float farBlend = 1.0 - smoothstep(
        shadowDistanceRange.y * (0.90 - cascadeBlend * 0.12),
        shadowDistanceRange.y * (1.00 + cascadeBlend * 0.08),
        worldDistance);
    float trueShadowVisibility = mix(1.0, mix(1.0, shadowFarVisibility, farBlend), shadowMapMix);
    trueShadowVisibility = mix(trueShadowVisibility, shadowNearVisibility, nearBlend * shadowMapMix);
    float farShadowResolveWeight = shadowMapStrength * (1.0 - farShadowProxy * shadowFarProxyStrength);
    directShadow = mix(directShadow, directShadow * mix(1.0, trueShadowVisibility, farShadowResolveWeight), shadowMapMix);
    shadowPresence = clamp(shadowPresence + farShadowProxy * shadowFarProxyStrength * (0.06 + horizonDepth * 0.08 + (1.0 - reliefAccent) * 0.05), 0.0, 1.0);
    shadowContour = clamp(shadowContour + farShadowProxy * shadowFarProxyStrength * (0.05 + horizonDepth * 0.06), 0.0, 0.40);
    distanceShadowLift = clamp(distanceShadowLift + farShadowProxy * shadowFarProxyStrength * 0.04, 0.0, 0.32);
    float lightMix = baseShade
        * mix(1.0, wrapDiffuse, clamp(shaderStrength * 0.88, 0.0, 1.0))
        * mix(1.0, directShadow, clamp(shaderStrength, 0.0, 1.0))
        * terrainShadow
        * reliefLift
        * skyBounce
        * ambientLift;
    float ambientFloor = mix(0.015, 0.18, clamp(sourcePresence * 0.90 + reliefAccent * 0.10, 0.0, 1.0));
    lightMix = clamp(lightMix, ambientFloor, 1.28);

    float materialBrightness = 1.0
        + grassMask * (0.04 + materialSeparationStrength * 0.05)
        - dirtMask * (0.02 + materialSeparationStrength * 0.01)
        - stoneMask * (0.04 + materialSeparationStrength * 0.03)
        + woodMask * (0.01 + materialSeparationStrength * 0.03)
        + leavesMask * (0.01 + materialSeparationStrength * 0.01);
    float materialWarmth = grassMask * (0.01 + materialSeparationStrength * 0.02)
        + dirtMask * (0.05 + materialSeparationStrength * 0.05)
        + woodMask * (0.07 + materialSeparationStrength * 0.06);
    float materialCoolness = stoneMask * (0.08 + materialSeparationStrength * 0.06)
        + leavesMask * (0.02 + materialSeparationStrength * 0.02);
    vec3 materialTint = vec3(1.0);
    materialTint += grassMask * vec3(-0.03, 0.07, -0.02) * materialSeparationStrength;
    materialTint += dirtMask * vec3(0.05, 0.01, -0.04) * materialSeparationStrength;
    materialTint += stoneMask * vec3(-0.05, -0.02, 0.06) * materialSeparationStrength;
    materialTint += woodMask * vec3(0.08, 0.03, -0.05) * materialSeparationStrength;
    materialTint += leavesMask * vec3(-0.05, 0.06, -0.03) * materialSeparationStrength;
    vec3 shadowMaterialTint = vec3(1.0);
    shadowMaterialTint += grassMask * vec3(-0.04, 0.06, -0.02) * materialShadowStrength;
    shadowMaterialTint += dirtMask * vec3(0.05, 0.00, -0.04) * materialShadowStrength;
    shadowMaterialTint += stoneMask * vec3(-0.05, -0.02, 0.06) * materialShadowStrength;
    shadowMaterialTint += woodMask * vec3(0.07, 0.01, -0.06) * materialShadowStrength;
    shadowMaterialTint += leavesMask * vec3(-0.05, 0.05, -0.03) * materialShadowStrength;

    vec3 viewDir = normalize(cameraPos - fragWorldPos);
    float rim = pow(1.0 - max(dot(viewDir, normal), 0.0), 2.0);
    float sunScatter = pow(max(dot(viewDir, sunDir), 0.0), 11.0) * (0.16 + sunVisibility * 0.26 + reliefAccent * 0.08) * warmLightStrength * (0.88 + sunScatterStrength * 0.55);
    float foliageTranslucency = foliageMask * foliageTranslucencyStrength * max(dot(-viewDir, sunDir), 0.0) * (0.18 + sunVisibility * 0.22);
    float microOcclusion = clamp((1.0 - baseShade) * (0.14 + (1.0 - sunVisibility) * 0.10 + stoneMask * 0.06 + woodMask * 0.04), 0.0, 0.20);
    float viewMaterial = clamp((rim * 0.34 + materialWarmth * 0.22 + materialCoolness * 0.18 + foliageMask * 0.10) * viewMaterialStrength, 0.0, 0.24);
    vec3 lit = albedo.rgb * lightMix * materialBrightness;
    lit *= materialTint;
    lit = mix(lit, lit * shadowMaterialTint, shadowPresence * 0.55);
    lit = mix(lit, lit * vec3(0.88, 0.92, 0.98), shadowContour);
    lit = mix(lit, lit * vec3(0.86, 0.90, 0.94), microOcclusion * (0.64 + foliageMask * 0.18));
    vec3 shadowTint = mix(vec3(1.0), vec3(0.84, 0.91, 1.06), coolShadowStrength * (1.0 - sunVisibility) * (0.55 + stoneMask * 0.25 + leavesMask * 0.12));
    lit *= shadowTint;
    lit = mix(lit, lit * vec3(1.03, 1.02, 0.99), reliefBridge);
    lit = mix(lit, lit * vec3(1.02, 1.01, 0.98), lightPlasticity);
    lit = mix(lit, lit * vec3(0.90, 0.94, 1.02), horizonDepth * 0.30);
    lit = mix(lit, lit * vec3(0.96, 0.98, 1.02), distanceShadowLift);
    lit = mix(lit, lit * vec3(0.92, 0.95, 1.00), distantSilhouette);
    lit = mix(lit, lit * vec3(1.03, 1.01, 0.97), materialWarmth * warmLightStrength);
    lit = mix(lit, lit * vec3(0.97, 1.00, 1.05), materialCoolness * coolShadowStrength);
    lit = mix(lit, lit * vec3(1.01, 1.00, 0.99), viewMaterial);
    vec3 skyTint = mix(vec3(1.0), vec3(0.84, 0.92, 1.04), skyBlendStrength * (0.32 + reliefAccent * 0.28));
    lit = mix(lit, lit * skyTint, 0.40 + ambientLiftStrength * 0.10);
    vec3 skyContourTint = mix(vec3(0.90, 0.96, 1.04), vec3(1.05, 1.00, 0.92), sunVisibility * 0.34 + reliefAccent * 0.16);
    float skyContour = clamp((horizonDepth * 0.38 + (1.0 - sunVisibility) * 0.10) * skyContourStrength, 0.0, 0.26);
    lit = mix(lit, lit * skyContourTint, skyContour);
    vec3 skyResponseTint = mix(vec3(0.92, 0.97, 1.03), vec3(1.05, 1.01, 0.95), sunVisibility * 0.42 + reliefAccent * 0.12);
    float skyResponse = clamp((0.04 + sourcePresence * 0.12 + sunVisibility * 0.10 + (1.0 - shadowPresence) * 0.04) * skyResponseStrength, 0.0, 0.24);
    lit = mix(lit, lit * skyResponseTint, skyResponse);
    lit += vec3(0.024, 0.032, 0.046) * rim * shaderStrength * (0.84 + glowStrength * 0.34 + ambientLiftStrength * 0.18);
    lit += vec3(0.086, 0.066, 0.042) * sunScatter * (0.92 + glowStrength * 0.32);
    lit += vec3(0.056, 0.084, 0.042) * foliageTranslucency;

    float fogStart = min(fogRange.x, fogRange.y);
    float fogEnd = max(fogRange.x, fogRange.y);
    float fogFactor = 0.0;
    if (fogEnd > fogStart)
    {
        fogFactor = clamp((distance(cameraPos, fragWorldPos) - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
        fogFactor = fogFactor * fogFactor * (3.0 - 2.0 * fogFactor);
    }

    float heightFog = clamp((cameraPos.y - fragWorldPos.y) * 0.022, 0.0, 1.0);
    float foliageFogResistance = foliageMask * 0.18 + grassMask * 0.05;
    float atmosphereFog = clamp(fogFactor * (0.28 + shaderStrength * 0.42 + ambientLiftStrength * 0.08 + hazeStrength * 0.08) + heightFog * atmosphereStrength * (0.16 + ambientLiftStrength * 0.04 + hazeStrength * 0.03), 0.0, 1.0);
    float atmosphereGradient = clamp((fogFactor * 0.44 + heightFog * 0.36) * atmosphereGradientStrength, 0.0, 0.34);
    float atmosphericContour = clamp((fogFactor * 0.30 + horizonDepth * 0.18 + heightFog * 0.14) * atmosphericContourStrength, 0.0, 0.28);
    float shadowHazeFusion = clamp((fogFactor * 0.28 + shadowPresence * 0.20 + horizonDepth * 0.18) * shadowHazeFusionStrength, 0.0, 0.26);
    float farReadability = clamp((fogFactor * 0.24 + distantSilhouette * 0.30 + horizonDepth * 0.18 + (1.0 - baseShade) * 0.08) * farReadabilityStrength, 0.0, 0.24);
    float shadowCascadeCohesion = clamp((fogFactor * 0.18 + horizonDepth * 0.14 + shadowPresence * 0.16) * shadowCascadeBlendStrength, 0.0, 0.22);
    float distanceMaterialFog = clamp(fogFactor * distanceMaterialStrength * (0.34 + stoneMask * 0.16 + foliageMask * 0.12 + woodMask * 0.10), 0.0, 1.0);
    vec3 materialDistanceTint = vec3(1.0);
    materialDistanceTint += stoneMask * vec3(-0.06, -0.01, 0.08) * distanceMaterialStrength;
    materialDistanceTint += woodMask * vec3(0.07, 0.01, -0.05) * distanceMaterialStrength;
    materialDistanceTint += dirtMask * vec3(0.05, 0.00, -0.04) * distanceMaterialStrength;
    materialDistanceTint += grassMask * vec3(-0.02, 0.05, -0.02) * distanceMaterialStrength;
    materialDistanceTint += leavesMask * vec3(-0.03, 0.05, -0.01) * distanceMaterialStrength;
    vec3 secondaryBounceTint = mix(vec3(0.94, 0.98, 1.02), vec3(1.04, 1.00, 0.94), sunVisibility * 0.46 + reliefAccent * 0.18);
    float secondaryBounce = clamp((0.10 + sunVisibility * 0.18 + reliefAccent * 0.12 + foliageMask * 0.08) * secondaryBounceStrength, 0.0, 0.42);
    float farWorldCohesion = clamp((fogFactor * 0.28 + distantSilhouette * 0.22 + atmosphericContour * 0.14 + skyResponse * 0.16) * farWorldCohesionStrength, 0.0, 0.26);
    lit = mix(lit, lit * secondaryBounceTint, secondaryBounce);
    lit *= mix(vec3(1.0), materialDistanceTint, distanceMaterialFog * 0.54);
    lit = mix(lit, fogColor.rgb, atmosphereFog * (1.0 - foliageFogResistance));
    lit = mix(lit, mix(lit, fogColor.rgb, 0.08 + hazeStrength * 0.10), fogFactor * hazeStrength * (0.24 - foliageFogResistance * 0.10));
    lit = mix(lit, fogColor.rgb * vec3(0.96, 0.99, 1.03), fogFactor * horizonDepthStrength * 0.10);
    lit = mix(lit, lit * vec3(0.94, 0.98, 1.04), atmosphereGradient);
    lit = mix(lit, lit * vec3(0.93, 0.97, 1.02), atmosphericContour);
    lit = mix(lit, lit * vec3(0.95, 0.98, 1.03), shadowHazeFusion);
    lit = mix(lit, lit * vec3(0.98, 1.00, 1.02), farReadability);
    lit = mix(lit, mix(lit, fogColor.rgb * vec3(0.97, 1.00, 1.03), 0.10), shadowCascadeCohesion);
    lit = mix(lit, lit * mix(vec3(0.95, 0.99, 1.03), vec3(1.02, 1.00, 0.97), sunVisibility * 0.24 + heightFog * 0.30), farWorldCohesion);
    vec3 farGradientTint = mix(vec3(0.90, 0.95, 1.02), vec3(1.03, 0.98, 0.92), heightFog * 0.44 + sunVisibility * 0.18);
    lit = mix(lit, lit * farGradientTint, clamp(fogFactor * farGradientStrength * 0.34, 0.0, 0.30));

    float luminance = dot(lit, vec3(0.2126, 0.7152, 0.0722));
    vec3 contrasted = vec3(0.5) + (lit - vec3(0.5)) * (1.0 + contrastStrength * 0.18);
    lit = mix(lit, contrasted, clamp(contrastStrength, 0.0, 1.0));
    lit = mix(vec3(luminance), lit, 1.04 + contrastStrength * 0.08 + materialSeparationStrength * 0.06);
    lit = mix(lit, mix(lit, fogColor.rgb * vec3(0.99, 1.01, 1.02), 0.08), clamp(finalCohesionStrength * (fogFactor * 0.26 + horizonDepth * 0.14 + skyResponse * 0.18), 0.0, 0.16));

    finalColor = vec4(lit, albedo.a);
}
