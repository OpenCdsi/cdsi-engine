using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using OpenCdsi.VaxEngine.Mobile.Data;
using OpenCdsi.VaxEngine.Mobile.Models;

namespace OpenCdsi.VaxEngine.Mobile.ViewModels;

public partial class PatientsViewModel : ObservableObject
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private List<Patient> _allPatients = new();

    public PatientsViewModel(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    [ObservableProperty]
    private ObservableCollection<Patient> patients = new();

    [ObservableProperty]
    private string searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private static async Task QuickForecastAsync()
        => await Shell.Current.GoToAsync("quickforecast");

    [RelayCommand]
    private static async Task AddPatientAsync()
        => await Shell.Current.GoToAsync("addpatient");

    [RelayCommand]
    private static async Task OpenPatientAsync(Patient? patient)
    {
        if (patient is null) return;
        // Absolute route with the id as a query parameter — see the note in
        // AppShell.xaml.cs about registering these routes with Shell.
        await Shell.Current.GoToAsync($"patientdetail?id={patient.Id}");
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        _allPatients = await db.Patients
            .OrderBy(p => p.LastName)
            .ThenBy(p => p.FirstName)
            .ToListAsync();

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allPatients
            : _allPatients.Where(p =>
                p.FullName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        Patients = new ObservableCollection<Patient>(filtered);
    }
}
