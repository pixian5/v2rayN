namespace v2rayN.Views;

public partial class CheckUpdateView
{
    public CheckUpdateView()
    {
        InitializeComponent();

        ViewModel = new CheckUpdateViewModel(UpdateViewHandler);

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.CheckUpdateModels, v => v.lstCheckUpdates.ItemsSource).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.EnableCheckPreReleaseUpdate, v => v.togEnableCheckPreReleaseUpdate.IsChecked).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoCheckUpdateTypeSelected, v => v.cmbAutoCheckUpdateType.SelectedIndex).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoCheckUpdateUtcHour, v => v.txtAutoCheckUpdateUtcHour.Text).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CheckUpdateCmd, v => v.btnCheckUpdate).DisposeWith(disposables);
        });
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        return await Task.FromResult(true);
    }
}
