using System;

namespace LaminarVR.AdaptiveMeditation.Environment
{
    public enum SceneBindingValidationCode
    {
        Valid,
        SceneIdMissing,
        ConfigurationMissing,
        ConfigurationInvalid,
        RequiredReferenceMissing,
        ShaderPropertyMissing
    }

    public sealed class SceneBindingValidation
    {
        private SceneBindingValidation(
            SceneBindingValidationCode code,
            string detail)
        {
            if (!Enum.IsDefined(typeof(SceneBindingValidationCode), code))
            {
                throw new ArgumentOutOfRangeException(nameof(code));
            }

            if (code != SceneBindingValidationCode.Valid
                && string.IsNullOrWhiteSpace(detail))
            {
                throw new ArgumentException(
                    "Invalid scene bindings require a diagnostic detail.",
                    nameof(detail));
            }

            Code = code;
            Detail = detail ?? string.Empty;
        }

        public SceneBindingValidationCode Code { get; }

        public string Detail { get; }

        public bool IsValid => Code == SceneBindingValidationCode.Valid;

        public static SceneBindingValidation Succeeded()
        {
            return new SceneBindingValidation(
                SceneBindingValidationCode.Valid,
                string.Empty);
        }

        public static SceneBindingValidation Failed(
            SceneBindingValidationCode code,
            string detail)
        {
            if (code == SceneBindingValidationCode.Valid)
            {
                throw new ArgumentException(
                    "Use Succeeded for valid scene bindings.",
                    nameof(code));
            }

            return new SceneBindingValidation(code, detail);
        }
    }
}
