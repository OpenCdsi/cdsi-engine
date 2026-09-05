using OpenCdsi.VaxEngine.Mobile.ViewModels;

namespace OpenCdsi.VaxEngine.Mobile.Views;

public partial class PatientsPage : ContentPage
{
    private readonly PatientsViewModel _viewModel;

    public PatientsPage(PatientsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Reload every time the page appears, not just once — returning
        // from "add dose" or "add patient" should show the new record
        // without a manual refresh.
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
