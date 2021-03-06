// THIS CODE AND INFORMATION IS PROVIDED "AS IS" WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO
// THE IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
// PARTICULAR PURPOSE.
//
// Copyright (c) Microsoft Corporation. All rights reserved


using System.Linq;
using System.Threading.Tasks;
using Kona.Infrastructure;
using System.Collections.Generic;
using Kona.UILogic.Models;
using Kona.UILogic.Repositories;
using Kona.UILogic.Services;
using Windows.UI.Xaml.Navigation;
using System.Collections.ObjectModel;

namespace Kona.UILogic.ViewModels
{
    public class ShippingAddressUserControlViewModel : ViewModel, IShippingAddressUserControlViewModel
    {
        private Address _address;
        private IReadOnlyCollection<ComboBoxItemValue> _states;
        private readonly ICheckoutDataRepository _checkoutDataRepository;
        private readonly ILocationService _locationService;
        private readonly IResourceLoader _resourceLoader;

        public ShippingAddressUserControlViewModel(ICheckoutDataRepository checkoutDataRepository, ILocationService locationService, IResourceLoader resourceLoader)
        {
            _address = new Address();
            _checkoutDataRepository = checkoutDataRepository;
            _locationService = locationService;
            _resourceLoader = resourceLoader;
        }

        [RestorableState]
        public Address Address
        {
            get { return _address; }
            set { SetProperty(ref _address, value); }
        }

        public IReadOnlyCollection<ComboBoxItemValue> States
        {
            get { return _states; }
            set { SetProperty(ref _states, value); }
        }

        public override async void OnNavigatedTo(object navigationParameter, NavigationMode navigationMode, Dictionary<string, object> viewState)
        {
            // The States collection needs to be populated before setting the State property
            await PopulateStatesAsync();

            if (viewState != null)
            {
                base.OnNavigatedTo(navigationParameter, navigationMode, viewState);

                if (navigationMode == NavigationMode.Refresh)
                {
                    // Restore the errors collection manually
                    var errorsCollection = RetrieveEntityStateValue<IDictionary<string, ReadOnlyCollection<string>>>("errorsCollection", viewState);

                    if (errorsCollection != null)
                    {
                        _address.SetAllErrors(errorsCollection);
                    }
                }
            }

            if (navigationMode == NavigationMode.New)
            {
                var defaultAddress = _checkoutDataRepository.GetDefaultShippingAddress();
                if (defaultAddress != null)
                {
                    // Update the information and validate the values
                    Address.FirstName = defaultAddress.FirstName;
                    Address.MiddleInitial = defaultAddress.MiddleInitial;
                    Address.LastName = defaultAddress.LastName;
                    Address.StreetAddress = defaultAddress.StreetAddress;
                    Address.OptionalAddress = defaultAddress.OptionalAddress;
                    Address.City = defaultAddress.City;
                    Address.State = defaultAddress.State;
                    Address.ZipCode = defaultAddress.ZipCode;
                    Address.Phone = defaultAddress.Phone;

                    ValidateForm();
                }
            }
        }

        public override void OnNavigatedFrom(Dictionary<string, object> viewState, bool suspending)
        {
            base.OnNavigatedFrom(viewState, suspending);

            // Store the errors collection manually
            if (viewState != null)
            {
                AddEntityStateValue("errorsCollection", _address.GetAllErrors(), viewState);
            }
        }

        public async Task PopulateStatesAsync()
        {
            var items = new List<ComboBoxItemValue> { new ComboBoxItemValue() { Id = string.Empty, Value = _resourceLoader.GetString("State") } };
            var states = await _locationService.GetStatesAsync();

            items.AddRange(states.Select(state => new ComboBoxItemValue() { Id = state, Value = state }));
            States = new ReadOnlyCollection<ComboBoxItemValue>(items);

            // Select the first item on the list
            // But disable validation first, because we don't want to fire it at this point
            _address.IsValidationEnabled = false;
            _address.State = States.First().Id;
            _address.IsValidationEnabled = true;
        }

        public bool ValidateForm()
        {
            return _address.ValidateProperties();
        }

        public void ProcessForm()
        {
            _checkoutDataRepository.SaveShippingAddress(Address);
        }
    }
}
