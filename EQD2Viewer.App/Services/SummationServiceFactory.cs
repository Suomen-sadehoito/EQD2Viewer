using EQD2Viewer.Core.Data;
using EQD2Viewer.Core.Interfaces;
using System.Collections.Generic;

namespace EQD2Viewer.App.Services
{
    /// <summary>
    /// Factory that creates <see cref="SummationService"/> instances.
    /// Registered in the composition root (<see cref="AppLauncher"/>)
    /// and injected into <see cref="UI.ViewModels.MainViewModel"/>.
 /// </summary>
    public class SummationServiceFactory : ISummationServiceFactory
  {
        public ISummationService Create(
        VolumeData referenceCtImage,
   ISummationDataLoader dataLoader,
     List<RegistrationData> registrations)
   {
            return new SummationService(referenceCtImage, dataLoader, registrations);
    }
    }
}
