﻿using System;
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
        private readonly Dictionary<String, InAppPurchaseData> _purchasedData;
        private readonly ReadOnlyCollection<ProductDefinition> _productDefinitions;

        private readonly Action _onProductsLoadedCallback;

        private Int32 _currentProductTypeIndex;

        public HuaweiStoreProductsLoader(IIapClient iapClient, IStoreCallback storeEvents, Dictionary<String, ProductInfo> productsInfo, Dictionary<String, InAppPurchaseData> purchasedData, ReadOnlyCollection<ProductDefinition> productDefinitions, Action onProductsLoadedCallback)
        {
            _iapClient = iapClient;
            _storeEvents = storeEvents;
            _productsInfo = productsInfo;
            _purchasedData = purchasedData;
            _productDefinitions = productDefinitions;
            _onProductsLoadedCallback = onProductsLoadedCallback;
        }

        public void Start()
        {
            _currentProductTypeIndex = 0;
            LoadProductTypeData(_productTypes[_currentProductTypeIndex]);
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
        
        private void LoadProductTypeData(ProductType productType)
        {
            var huaweiProductType = GetHuaweiProductType(productType);
            var productTypeIds = _productDefinitions.Where(c => c.type == productType).Select(c => c.storeSpecificId).ToList();
            if (productTypeIds.Count > 0)
            {
                RequestProductsInfo(productTypeIds, huaweiProductType);
            }
            else
            {
                FinishCurrentProductTypeDataLoading();
            }
        }

        private void ProcessProductInfos(ProductInfoResult result)
        {
            if (result == null)
            {
                return;
            }

            Debug.Log($"[HuaweiStore] Loaded product infos:\n{String.Join("Product Id: \n", result.ProductInfoList.Select(productInfo => productInfo.ProductId))}");

            foreach (var productInfo in result.ProductInfoList)
            {
                _productsInfo.Add(productInfo.ProductId, productInfo);
            }

            LoadCurrentProductTypePurchasesData(_productTypes[_currentProductTypeIndex]);
        }

        private void FinishCurrentProductTypeDataLoading()
        {
            _currentProductTypeIndex++;
            if (_currentProductTypeIndex < _productTypes.Length)
            {
                LoadProductTypeData(_productTypes[_currentProductTypeIndex]);
            }
            else
            {
                _onProductsLoadedCallback();
            }
        }

        private void RequestProductsInfo(IList<String> consumablesIDs, PriceType type)
        {
            var productsDataRequest = new ProductInfoReq
            {
                PriceType = type,
                ProductIds = consumablesIDs,
            };

            _iapClient.ObtainProductInfo(productsDataRequest)
                      .AddOnFailureListener(GetProductsFailure)
                      .AddOnSuccessListener(ProcessProductInfos);
        }

        private void GetProductsFailure(HMSException exception)
        {
            Debug.LogError($"[HuaweiStore]: ERROR on GetProductsFailure: {exception.WrappedCauseMessage} | {exception.WrappedExceptionMessage}");
            _storeEvents.OnSetupFailed(InitializationFailureReason.NoProductsAvailable);
        }

        private void LoadCurrentProductTypePurchasesData(ProductType productType)
        {
            RequestProductTypePurchasesData(GetHuaweiProductType(productType));
        }

        private void RequestProductTypePurchasesData(PriceType type)
        {
            var ownedPurchasesReq = new OwnedPurchasesReq
            {
                PriceType = type,
            };

            _iapClient.ObtainOwnedPurchases(ownedPurchasesReq)
                      .AddOnSuccessListener(ProcessPurchasesData);
        }

        private void ProcessPurchasesData(OwnedPurchasesResult result)
        {
            if (result is { InAppPurchaseDataList: not null })
            {
                Debug.Log($"[HuaweiStore] Loaded owned-product infos:\n{String.Join("Product Id: \n", result.InAppPurchaseDataList.Select(productInfo => productInfo.ProductId))}");

                foreach (var inAppPurchaseData in result.InAppPurchaseDataList)
                {
                    _purchasedData[inAppPurchaseData.ProductId] = inAppPurchaseData;
                }
            }
            
            FinishCurrentProductTypeDataLoading();
        }
    }
}