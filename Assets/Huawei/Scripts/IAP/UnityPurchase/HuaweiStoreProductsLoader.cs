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
        private readonly IIapClient _iapClient;
        private readonly IStoreCallback _storeEvents;
        private readonly Dictionary<String, ProductInfo> _productsInfo;
        private readonly ReadOnlyCollection<ProductDefinition> _productDefinitions;

        private readonly Action _onProductsLoadedCallback;

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
            LoadConsumableProducts();
        }
        
        private void LoadConsumableProducts()
        {
            var consumablesIDs = _productDefinitions.Where(c => c.type == ProductType.Consumable).Select(c => c.storeSpecificId).ToList();
            CreateProductRequest(consumablesIDs, PriceType.IN_APP_CONSUMABLE, LoadNonConsumableProducts);
        }
        
        private void LoadNonConsumableProducts()
        {
            var nonConsumablesIDs = _productDefinitions.Where(c => c.type == ProductType.NonConsumable).Select(c => c.storeSpecificId).ToList();
            if (nonConsumablesIDs.Count > 0)
            {
                CreateProductRequest(nonConsumablesIDs, PriceType.IN_APP_NONCONSUMABLE, LoadSubscribeProducts);
            }
            else
            {
                LoadSubscribeProducts();
            }
        }
        
        private void LoadSubscribeProducts()
        {
            var subscribeIDs = _productDefinitions.Where(c => c.type == ProductType.Subscription).Select(c => c.storeSpecificId).ToList();
            if (subscribeIDs.Count > 0)
            {
                CreateProductRequest(subscribeIDs, PriceType.IN_APP_SUBSCRIPTION, _onProductsLoadedCallback);
            }
            else
            {
                _onProductsLoadedCallback();
            }
        }

        private void CreateProductRequest(IList<String> consumablesIDs, PriceType type, Action onSuccess)
        {
            var productsDataRequest = new ProductInfoReq
            {
                PriceType = type,
                ProductIds = consumablesIDs,
            };

            _iapClient.ObtainProductInfo(productsDataRequest)
                      .AddOnFailureListener(GetProductsFailure)
                      .AddOnSuccessListener(result => { ProcessLoadedProductInfos(result); onSuccess(); });
        }

        private void GetProductsFailure(HMSException exception)
        {
            Debug.LogError("[HuaweiStore]: ERROR on GetProductsFailure: " + exception.WrappedCauseMessage + " " + exception.WrappedExceptionMessage);
            _storeEvents.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable);
        }

        private void ProcessLoadedProductInfos(ProductInfoResult result)
        {
            if (result == null || result.ProductInfoList.Count == 0)
            {
                return;
            }

            Debug.Log($"[HuaweiStore] Loaded product infos:\n{String.Join("Product Id: \n", result.ProductInfoList.Select(productInfo => productInfo.ProductId))}");

            foreach (var productInfo in result.ProductInfoList)
            {
                _productsInfo.Add(productInfo.ProductId, productInfo);
            }
        }
    }
}