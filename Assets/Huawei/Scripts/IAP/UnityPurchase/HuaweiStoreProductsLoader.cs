using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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
        
        private Int32 _currentProductTypeIndex;

        public HuaweiStoreProductsLoader(IIapClient iapClient, IStoreCallback storeEvents, Dictionary<String, ProductInfo> productsInfo, Dictionary<String, InAppPurchaseData> purchasedData, ReadOnlyCollection<ProductDefinition> productDefinitions)
        {
            _iapClient = iapClient;
            _storeEvents = storeEvents;
            _productsInfo = productsInfo;
            _purchasedData = purchasedData;
            _productDefinitions = productDefinitions;
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
                _ => throw new ArgumentOutOfRangeException(nameof(productType), productType, null),
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

        private void FinishCurrentProductTypeDataLoading()
        {
            _currentProductTypeIndex++;
            if (_currentProductTypeIndex < _productTypes.Length)
            {
                LoadProductTypeData(_productTypes[_currentProductTypeIndex]);
            }
            else
            {
                _storeEvents.OnProductsRetrieved(_productsInfo.Values.Select(GetProductDescriptionFromProductInfo).ToList());
            }
        }
        
        private ProductDescription GetProductDescriptionFromProductInfo(ProductInfo productInfo)
        {
            var price = productInfo.MicrosPrice * 0.000001f;

            var priceString = $"{productInfo.Currency} {(price < 100 ? price.ToString("0.00") : ((Int32)(price + 0.5f)).ToString())}";
            var metadata = new ProductMetadata(priceString, productInfo.ProductName, productInfo.ProductDesc, productInfo.Currency, (Decimal)price);

            return _purchasedData.TryGetValue(productInfo.ProductId, out var purchaseData)
                ? new ProductDescription(productInfo.ProductId, metadata, CreateReceipt(purchaseData), purchaseData.OrderID)
                : new ProductDescription(productInfo.ProductId, metadata);
        }
        
        private static String CreateReceipt(InAppPurchaseData purchaseData)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{').Append("\"Store\":\"AppGallery\",\"TransactionID\":\"").Append(purchaseData.OrderID).Append("\", \"Payload\":{ ");
            sb.Append("\"product\":\"").Append(purchaseData.ProductId).Append("\"");
            sb.Append('}');
            sb.Append('}');
            return sb.ToString();
        }
        
        private void RequestProductsInfo(IList<String> consumablesIDs, PriceType type)
        {
            var productsDataRequest = new ProductInfoReq
            {
                PriceType = type,
                ProductIds = consumablesIDs,
            };

            _iapClient.ObtainProductInfo(productsDataRequest)
                      .AddOnFailureListener(ProcessProductInfoLoadingFailure)
                      .AddOnSuccessListener(ProcessProductsInfo);
        }

        private void ProcessProductsInfo(ProductInfoResult result)
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

            RequestProductTypePurchasesData(GetHuaweiProductType(_productTypes[_currentProductTypeIndex]));
        }

        private void ProcessProductInfoLoadingFailure(HMSException exception)
        {
            Debug.LogError($"[HuaweiStore]: ERROR on RequestProductsInfo: {exception.WrappedCauseMessage} | {exception.WrappedExceptionMessage}");
            _storeEvents.OnSetupFailed(InitializationFailureReason.NoProductsAvailable);
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