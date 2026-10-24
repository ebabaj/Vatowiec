using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class DesktopTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PasswordResetSuccessMessageWaitsForApiAndDistinguishesGeneratedFromCustom(bool generated)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new FakeApi { InstallationAdmin = true, ResetGate = gate };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenUsersSettings(); model.SelectedAdminUser = model.AdminUsers[0];
        var reset = model.ResetAdminPassword(generated ? null : "CustomFixture!2a");
        Assert.True(model.IsBusy); Assert.Equal("", model.Message); Assert.False(reset.IsCompleted);
        gate.SetResult(); var response = await reset;
        Assert.NotNull(response); Assert.Equal(generated, response.IsGenerated); Assert.True(response.User.User.MustChangePassword);
        Assert.False(model.HasUnsavedChanges);
        Assert.Equal(generated ? "Zresetowano hasło i unieważniono sesje. Wymagana zmiana hasła przy logowaniu."
            : "Hasło użytkownika zostało zmienione. Przy następnym logowaniu użytkownik będzie musiał ustawić własne hasło", model.Message);
    }
    [Fact]
    public async Task FailedCustomResetDoesNotReturnPasswordOrKeepPreviousSuccessMessage()
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenUsersSettings(); model.SelectedAdminUser = model.AdminUsers[0];
        Assert.NotNull(await model.ResetAdminPassword("FirstFixture!2a"));
        api.ResetError = new ApiException(400, "Hasło nie spełnia aktualnej polityki.");
        Assert.Null(await model.ResetAdminPassword("RejectedFixture"));
        Assert.Equal("Hasło nie spełnia aktualnej polityki.", model.Message);
        Assert.DoesNotContain("zostało zmienione", model.Message);
    }
    private sealed partial class FakeApi
    {
        public TaskCompletionSource? ResetGate;
        public ApiException? ResetError;
        public async Task<TemporaryPasswordResponse> ResetUserPassword(Guid id, UserVersionRequest request)
        {
            if (ResetGate != null) await ResetGate.Task;
            if (ResetError != null) throw ResetError;
            var previous = await UserDetail(id);
            adminDetail = previous with { User = previous.User with { MustChangePassword = true }, ConcurrencyStamp = Guid.NewGuid().ToString() };
            return new(adminDetail, request.TemporaryPassword ?? "AbcD2345EfGh678!", request.TemporaryPassword == null);
        }
    }
}
