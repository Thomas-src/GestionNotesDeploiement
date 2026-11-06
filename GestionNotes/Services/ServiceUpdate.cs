using GestionNotes.Models;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace GestionNotes.Services
{
    public class ServiceUpdate
    {
        public WaitingResult? PendingUpdate { get; private set; }

        private readonly string _updateFolder;

        public ServiceUpdate(IOptions<AppSettings> options)
        {
            _updateFolder = options.Value.UpdateFile;
        }
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
        private VersionManifest? _versionManifest;

       
        public UpdateResult CheckVersion()
        {
            string file = Path.Combine(_updateFolder, "version.json");

            if (!File.Exists(file))
            {
                Debug.WriteLine($"Fichier introuvable : {file}");
                return new UpdateResult(UpdateStatus.ERROR, Message: $"Le fichier {file} est introuvable.");
            }
            try
            {
                string json = File.ReadAllText(file);
                _versionManifest = JsonSerializer.Deserialize<VersionManifest>(json, JsonOptions);
                if (_versionManifest is null || string.IsNullOrWhiteSpace(_versionManifest.Version) || string.IsNullOrWhiteSpace(_versionManifest.File))
                {
                    return new UpdateResult(UpdateStatus.ERROR, Message: "Le manifeste est incomplet (propriétés « version » et « file » requises).");
                }
            }
            catch (Exception)
            {
                return new UpdateResult(UpdateStatus.ERROR, Message: "Impossible de lire le manifeste de version.");
            }

            if (_versionManifest is null || string.IsNullOrWhiteSpace(_versionManifest.Version))
            {
                return new UpdateResult(UpdateStatus.ERROR, Message: "Le manifeste est incomplet (propriété « version » requise).");
            }


            return CompareVersion();
        }

        public UpdateResult CheckUpdateFolder()
        {
            if (_versionManifest is null)  { 
                return new UpdateResult(UpdateStatus.ERROR, Message: "Aucun manifeste de version n'a été chargé.");
            }
            string file = Path.Combine(_updateFolder, _versionManifest.File);

            if (!File.Exists(file))
            {
                return new UpdateResult(UpdateStatus.ERROR, Message: "L'archive zip contenant la nouvelle mise à jours est introuvable.");
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "GestionNotes", $"update-{_versionManifest.Version}");
            string destination = Path.Combine(tempDir, _versionManifest.File);

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }

                Directory.CreateDirectory(tempDir);
                File.Copy(file, destination, overwrite: true);

                if (!File.Exists(destination) ||new FileInfo(destination).Length != new FileInfo(file).Length)
                {
                    throw new IOException("La taille de la copie ne correspond pas à celle de l'original.");
                }


            } catch(Exception)
            {
                return new UpdateResult(UpdateStatus.ERROR, Message: "La copie de la mise à jour a échoué.");
            }

            PendingUpdate = new WaitingResult(tempDir, destination, _versionManifest.Version);

            return new UpdateResult(UpdateStatus.OK, Message: "La copie de la mise à jour a été un succès.");

        }

        private UpdateResult CompareVersion()
        {
            if (!Version.TryParse(_versionManifest.Version, out Version? versionAvailable))
            {
                return new UpdateResult(UpdateStatus.ERROR, Message: "Le numéro de version du manifest est invalide.");
            }

            var currentVersion = GetCurrentVersion;


            if (versionAvailable > currentVersion)
            {
                return new UpdateResult(UpdateStatus.UPDATE_AVAILABLE, Message: $"Mise à jour ({versionAvailable}) disponible (version actuelle: {currentVersion}). \nVoulez-vous mettre à jour l'application ?");
            }
            else
            {
                return new UpdateResult(UpdateStatus.NO_UPDATE_AVAILABLE, Message: "Aucune mise à jour disponible.");
            }
        }

        public UpdateResult LaunchUpdater()
        {
            if (PendingUpdate is null)
            {
                return new UpdateResult(UpdateStatus.ERROR, Message: "Aucune mise à jour n'est prête à être appliquée.");
            }

            try
            {
                string installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

                if (!File.Exists(Path.Combine(installDir, "Updater.exe")))
                {
                    return new UpdateResult(UpdateStatus.ERROR, Message: "Updater.exe est introuvable.");
                }

                string updaterDir = Path.Combine(PendingUpdate.FolderPath, "updater");
                Directory.CreateDirectory(updaterDir);

                foreach (string file in Directory.GetFiles(installDir, "Updater.*"))
                {
                    File.Copy(file, Path.Combine(updaterDir, Path.GetFileName(file)), overwrite: true);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(updaterDir, "Updater.exe"),
                    WorkingDirectory = updaterDir,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add("--pid");
                startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                startInfo.ArgumentList.Add("--zip");
                startInfo.ArgumentList.Add(PendingUpdate.ZipPath);
                startInfo.ArgumentList.Add("--install-dir");
                startInfo.ArgumentList.Add(installDir);
                startInfo.ArgumentList.Add("--exe");
                startInfo.ArgumentList.Add(Path.GetFileName(Environment.ProcessPath!));

                Process.Start(startInfo);
                return new UpdateResult(UpdateStatus.OK, Message: "Updater lancé.");
            }
            catch (Exception)
            {
                return new UpdateResult(UpdateStatus.ERROR, Message: "Impossible de lancer le programme de mise à jour.");
            }
        }

        private Version GetCurrentVersion => GetType().Assembly.GetName().Version;

        private record VersionManifest(string Version, string File);

        public record UpdateResult(UpdateStatus Status, string Message)
        {
        }

        public record WaitingResult(string FolderPath, string ZipPath, string Version);
    }

    public enum UpdateStatus
    {
        UPDATE_AVAILABLE,
        ERROR,
        NO_UPDATE_AVAILABLE,
        OK
    }

}
