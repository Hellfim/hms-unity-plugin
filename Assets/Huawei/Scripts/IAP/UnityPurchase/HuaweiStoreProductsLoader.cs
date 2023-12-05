using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HuaweiMobileServices.IAP;
using HuaweiMobileServices.Utils;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace HmsPlugin
{
    public class HuaweiStoreProductsLoader
    {
        private readonly ProductType[] _productTypes = { ProductType.Consumable, ProductType.NonConsumable, ProductType.Subscription };
        
        private readonly IIapClient _iapClient;
        private readonly IStoreCallback _storeEvents;
        private readonly Dictionary<String, ProductInfo> _productsInfo;
        private readonly ReadOnlyCollection<ProductDefinition> _productDefinitions;

        private readonly Action _onProductsLoadedCallback;

        private Int32 _currentLoadableProductIndex;

        public HuaweiStoreProductsLoader(IIapClient iapClient, IStoreCallback storeEvents, Dictionary<String, ProductInfo> productsInfo, ReadOnlyCollection<ProductDefinition> productDefinitions, Action onProductsLoadedCallback)
        {
            _iapClient = iapClient;
            _storeEvents = storeEvents;
            _productsInfo = productsInfo;
            _productDefinitions = productDefinitions;
            _onProductsLoadedCallback = onProductsLoadedCallback;
        }

        public void Start()
        {
            _currentLoadableProductIndex = 0;
            LoadProductsByType(_productTypes[_currentLoadableProductIndex]);
        }

        private static PriceType GetHuaweiProductType(ProductType productType)
        {
            return productType switch
            {
                ProductType.Consumable => PriceType.IN_APP_CONSUMABLE,
                ProductType.NonConsumable => PriceType.IN_APP_NONCONSUMABLE,
                ProductType.Subscription => PriceType.IN_APP_SUBSCRIPTION,
                _ => throw new ArgumentOutOfRangeException(nameof(productType), productType, null)
            };
        }
        
        private void LoadProductsByType(ProductType productType)
        {
            var huaweiProductType = GetHuaweiProductType(productType);
            var productTypeIds = _productDefinitions.Where(c => c.type == productType).Select(c => c.storeSpecificId).ToList();
            if (productTypeIds.Count > 0)
            {
                CreateProductRequest(productTypeIds, huaweiProductType);
            }
            else
            {
                FinishProductLoading();
            }
        }

        private void ProcessLoadedProducts(ProductInfoResult result)
        {
            if (result != null)
            {
                Debug.Log($"[HuaweiStore] Loaded product infos:\n{String.Join("Product Id: \n", result.ProductInfoList.Select(productInfo => productInfo.ProductId))}");

                foreach (var productInfo in result.ProductInfoList)
                {
                    _productsInfo.Add(productInfo.ProductId, productInfo);
                }
            }

            FinishProductLoading();
        }

        private void FinishProductLoading()
        {
            _currentLoadableProductIndex++;
            if (_currentLoadableProductIndex < _productTypes.Length)
            {
                LoadProductsByType(_productTypes[_currentLoadableProductIndex]);
            }
            else
            {
                _onProductsLoadedCallback();
            }
        }

        private void CreateProductRequest(IList<String> consumablesIDs, PriceType type)
        {
            var productsDataRequest = new ProductInfoReq
            {
                PriceType = type,
                ProductIds = consumablesIDs,
            };

            _iapClient.ObtainProductInfo(productsDataRequest)
                      .AddOnFailureListener(GetProductsFailure)
                      .AddOnSuccessListener(ProcessLoadedProducts);
        }

        private void GetProductsFailure(HMSException exception)
        {
            Debug.LogError($"[HuaweiStore]: ERROR on GetProductsFailure: {exception.WrappedCauseMessage} | {exception.WrappedExceptionMessage}");
            _storeEvents.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable);
        }
    }
}