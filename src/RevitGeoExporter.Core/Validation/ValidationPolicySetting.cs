using System;

namespace RevitGeoExporter.Core.Validation;

public sealed class ValidationPolicySetting
{
    public ValidationPolicySetting(ValidationPolicyTarget target, ValidationSeverity severity)
    {
        Target = target;
        Severity = severity;
    }

    public ValidationPolicyTarget Target { get; set; }

    public ValidationSeverity Severity { get; set; }

    public ValidationPolicySetting Clone()
    {
        return new ValidationPolicySetting(Target, Severity);
    }
}
