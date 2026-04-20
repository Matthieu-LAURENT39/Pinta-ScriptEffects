using Pinta.Core;

namespace ScriptEffects
{
    [Mono.Addins.Extension]
    public class ScriptEffectsExtension : IExtension
    {
        public void Initialize()
        {
            IServiceProvider services = PintaCore.Services;
            PintaCore.Effects.RegisterEffect(new ScriptEffect(services));
        }

        public void Uninitialize()
        {
            PintaCore.Effects.UnregisterInstanceOfEffect<ScriptEffect>();
        }
    }
}