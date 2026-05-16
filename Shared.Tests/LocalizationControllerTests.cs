using System.Globalization;
using System.Threading;
using GTAWParser.Shared;
using Xunit;

namespace GTAWParser.Shared.Tests
{
    /// <summary>
    /// LocalizationController holds static state, so these tests share a fixture
    /// to keep ordering deterministic. xUnit serializes tests within a class.
    /// </summary>
    [Collection("Localization")]
    public class LocalizationControllerTests
    {
        [Fact]
        public void GetCodeFromLanguage_English_ReturnsEnUs()
        {
            Assert.Equal("en-US", LocalizationController.GetCodeFromLanguage(LocalizationController.Language.English));
        }

        [Fact]
        public void GetCodeFromLanguage_Spanish_ReturnsEsEs()
        {
            Assert.Equal("es-ES", LocalizationController.GetCodeFromLanguage(LocalizationController.Language.Spanish));
        }

        [Fact]
        public void GetLanguageFromCode_EnUs_ReturnsEnglish()
        {
            Assert.Equal("English", LocalizationController.GetLanguageFromCode("en-US"));
        }

        [Fact]
        public void GetLanguageFromCode_EsEs_ReturnsSpanish()
        {
            Assert.Equal("Spanish", LocalizationController.GetLanguageFromCode("es-ES"));
        }

        [Fact]
        public void SetLanguage_Spanish_SetsThreadCulture()
        {
            LocalizationController.SetLanguage(LocalizationController.Language.Spanish);

            Assert.Equal("es-ES", Thread.CurrentThread.CurrentUICulture.Name);
            Assert.Equal("es-ES", LocalizationController.GetLanguage());
        }

        [Fact]
        public void SetLanguage_PersistCallback_ReceivesNewCode()
        {
            string? captured = null;
            LocalizationController.SetLanguage(LocalizationController.Language.Spanish, code => captured = code);

            Assert.Equal("es-ES", captured);
        }

        [Fact]
        public void InitializeLocale_WithSavedCode_AppliesIt()
        {
            // Reset to English first by calling SetLanguage, then re-initialize.
            LocalizationController.SetLanguage(LocalizationController.Language.English);

            LocalizationController.InitializeLocale("en-US");

            Assert.Equal("en-US", Thread.CurrentThread.CurrentUICulture.Name);
        }
    }
}
