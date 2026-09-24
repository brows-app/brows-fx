using Domore.Logs;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Brows.IO.Helpers;

internal sealed class FileDeleter {
    private static readonly ILog Log = Logging.For(typeof(FileDeleter));

    public static Task Delete(FileInfo file, CancellationToken cancellationToken) {
        if (file is null) {
            throw new ArgumentNullException(nameof(file));
        }
        if (Log.Debug()) {
            Log.Debug(nameof(Delete) + " > " + file.FullName);
        }
        return Task.Run(cancellationToken: cancellationToken, action: () => {
            file.Refresh();
            if (file.Exists == false) {
                return;
            }
            try {
                file.Delete();
            }
            catch (UnauthorizedAccessException exception) {
                if (Log.Info()) {
                    Log.Info(nameof(UnauthorizedAccessException) + " > " + file.FullName, exception);
                }
                try {
                    file.Attributes = FileAttributes.Normal;
                    file.Refresh();
                    if (file.Exists) {
                        file.Delete();
                    }
                }
                catch (FileNotFoundException) {
                }
                catch (DirectoryNotFoundException) {
                }
            }
            catch (FileNotFoundException) {
            }
            catch (DirectoryNotFoundException) {
            }
        });
    }
}
