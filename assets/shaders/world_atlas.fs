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

void main()
{
    vec4 albedo = texture(texture0, fragTexCoord) * fragColor * colDiffuse;
    if (albedo.a <= 0.01)
    {
        discard;
    }

    vec3 normal = normalize(fragNormal);
    vec3 sunDir = normalize(-sunDirection);
    float diffuse = max(dot(normal, sunDir), 0.0);
    float lightMix = mix(1.0, 0.84 + diffuse * 0.16, clamp(shaderStrength, 0.0, 1.0));

    vec3 viewDir = normalize(cameraPos - fragWorldPos);
    float rim = pow(1.0 - max(dot(viewDir, normal), 0.0), 2.0);
    vec3 lit = albedo.rgb * lightMix;
    lit += vec3(0.028, 0.034, 0.045) * rim * shaderStrength;

    float fogStart = min(fogRange.x, fogRange.y);
    float fogEnd = max(fogRange.x, fogRange.y);
    float fogFactor = 0.0;
    if (fogEnd > fogStart)
    {
        fogFactor = clamp((distance(cameraPos, fragWorldPos) - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
        fogFactor = fogFactor * fogFactor * (3.0 - 2.0 * fogFactor);
    }

    lit = mix(lit, fogColor.rgb, fogFactor * (0.42 + shaderStrength * 0.48));
    finalColor = vec4(lit, albedo.a);
}
