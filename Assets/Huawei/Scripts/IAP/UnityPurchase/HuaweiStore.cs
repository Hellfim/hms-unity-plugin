using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using HuaweiMobileServices.IAP;
using HuaweiMobileServices.Utils;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using System.Linq;
using System.Text;
using UnityEngine;

namespace HmsPlugin
{
    public class HuaweiStore : IStore
    {
        private static HuaweiStore _currentInstance;

        public static HuaweiStore GetInstance()
        {
            if (_currentInstance != null)
            {
                return _currentInstance;
            }

            _currentInstance = new HuaweiStore();
            return _currentInstance;
        }

        private System.Object _locker;
        private Dictionary<String, ProductInfo> _productsInfo;
        private Dictionary<String, InAppPurchaseData> _purchasedData;
        private ReadOnlyCollection<ProductDefinition> _productDefinitions;

        private Boolean _isClientInitialized;
        private IIapClient _iapClient;
        private IStoreCallback _storeEvents;
        
        void IStore.Initialize(IStoreCallback callback)
        {
            _storeEvents = callback;

            _locker = new System.Object();
            _productsInfo = new Dictionary<String, ProductInfo>(100);
            _purchasedData = new Dictionary<String, InAppPurchaseData>(50);
            
            InitializeClient();
        }

        void IStore.RetrieveProducts(ReadOnlyCollection<ProductDefinition> products)
        {
            lock (_locker)
            {
                Debug.Log($"[HuaweiStore] IAP RetrievedProducts:\n{String.Join("Product Id: \n", products.Select(definition => definition.id))}");

                _productDefinitions = products;

                LoadProductsInfos();
            }
        }

        void IStore.Purchase(ProductDefinition product, String developerPayload)
        {
            if (!_productsInfo.ContainsKey(product.storeSpecificId))
            {
                _storeEvents.OnPurchaseFailed(new PurchaseFailureDescription(product.id, PurchaseFailureReason.ProductUnavailable, "UnknownProduct"));
                return;
            }

            var productInfo = _productsInfo[product.storeSpecificId];
            var purchaseIntentReq = new PurchaseIntentReq
            {
                PriceType = productInfo.PriceType,
                ProductId = productInfo.ProductId,
                DeveloperPayload = developerPayload
            };

            _iapClient.CreatePurchaseIntent(purchaseIntentReq)
                      .AddOnSuccessListener(intentResult => { PurchaseIntentCreated(intentResult, product); })
                      .AddOnFailureListener(exception => { _storeEvents.OnPurchaseFailed(new PurchaseFailureDescription(product.id, PurchaseFailureReason.Unknown, exception.Message)); });
        }

        void IStore.FinishTransaction(ProductDefinition product, String transactionId)
        {
            if (_purchasedData.TryGetValue(product.storeSpecificId, out var data))
            {
                var token = data.PurchaseToken;
                var request = new ConsumeOwnedPurchaseReq
                {
                    PurchaseToken = token,
                };

                _iapClient.ConsumeOwnedPurchase(request)
                          .AddOnSuccessListener(_ => { _purchasedData.Remove(product.storeSpecificId); })
                          .AddOnFailureListener(exception => { Debug.Log("Consume failed " + exception.Message + " " + exception.StackTrace); });
            }
        }

        private void InitializeClient()
        {
            _iapClient = Iap.GetIapClient();
            Debug.Log("[HuaweiStore] IAP Client Created");

            _iapClient.EnvReady
                      .AddOnSuccessListener(ProcessInitializationSuccess)
                      .AddOnFailureListener(ProcessInitializationFailure);
        }

        private void ProcessInitializationSuccess(EnvReadyResult result)
        {
            Debug.Log("[HuaweiStore] IAP Client Success");
            lock (_locker)
            {
                _isClientInitialized = true;
                
                LoadProductsInfos();
            }
        }

        private void ProcessInitializationFailure(HMSException exception)
        {
            Debug.LogError("[HuaweiStore]: ERROR on ClientInitFailed: " + exception.WrappedCauseMessage + " " + exception.WrappedExceptionMessage);
            _storeEvents.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable);
        }

        private void LoadProductsInfos()
        {
            if (!_isClientInitialized || _productDefinitions == null)
            {
                return;
            }
            
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
                CreateProductRequest(subscribeIDs, PriceType.IN_APP_SUBSCRIPTION, ProductsLoaded);
            }
            else
            {
                ProductsLoaded();
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

        private void LoadOwnedConsumables()
        {
            CreateOwnedPurchaseRequest(PriceType.IN_APP_CONSUMABLE, LoadOwnedNonConsumables);
        }

        private void LoadOwnedNonConsumables()
        {
            CreateOwnedPurchaseRequest(PriceType.IN_APP_NONCONSUMABLE, LoadOwnedSubscribes);
        }

        private void LoadOwnedSubscribes()
        {
            CreateOwnedPurchaseRequest(PriceType.IN_APP_SUBSCRIPTION, ProductsLoaded);
        }

        private void CreateOwnedPurchaseRequest(PriceType type, Action onSuccess)
        {
            var ownedPurchasesReq = new OwnedPurchasesReq
            {
                PriceType = type,
            };

            _iapClient.ObtainOwnedPurchases(ownedPurchasesReq)
                      .AddOnSuccessListener(result => { ParseOwned(result); onSuccess(); });
        }

        private void ParseOwned(OwnedPurchasesResult result)
        {
            if (result == null || result.InAppPurchaseDataList == null)
            {
                return;
            }

            foreach (var inAppPurchaseData in result.InAppPurchaseDataList)
            {
                _purchasedData[inAppPurchaseData.ProductId] = inAppPurchaseData;

                Debug.Log("ProductId: " + inAppPurchaseData.ProductId + " , ProductName: " + inAppPurchaseData.ProductName);
            }
        }

        private void ProductsLoaded()
        {
            _storeEvents.OnProductsRetrieved(_productsInfo.Values.Select(GetProductDescriptionFromProductInfo).ToList());
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

        private void PurchaseIntentCreated(PurchaseIntentResult intentResult, ProductDefinition product)
        {
            if (intentResult == null)
            {
                _storeEvents.OnPurchaseFailed(new PurchaseFailureDescription(product.id, PurchaseFailureReason.Unknown, "IntentIsNull"));
                return;
            }

            var status = intentResult.Status;
            status.StartResolutionForResult(androidIntent =>
            {
                var purchaseResultInfo = _iapClient.ParsePurchaseResultInfoFromIntent(androidIntent);

                switch (purchaseResultInfo.ReturnCode)
                {
                    case OrderStatusCode.ORDER_STATE_SUCCESS:
                    {
                        _purchasedData[product.storeSpecificId] = purchaseResultInfo.InAppPurchaseData;

                        Debug.Log($"token {purchaseResultInfo.InAppPurchaseData.PurchaseToken}");

                        _storeEvents.OnPurchaseSucceeded(product.storeSpecificId, purchaseResultInfo.InAppDataSignature, purchaseResultInfo.InAppPurchaseData.OrderID);
                        break;
                    }
                    case OrderStatusCode.ORDER_PRODUCT_OWNED:
                    {
                        _storeEvents.OnPurchaseFailed(new PurchaseFailureDescription(product.storeSpecificId, PurchaseFailureReason.DuplicateTransaction, purchaseResultInfo.ErrMsg));
                        break;
                    }
                    case OrderStatusCode.ORDER_STATE_CANCEL:
                    {
                        _storeEvents.OnPurchaseFailed(new PurchaseFailureDescription(product.storeSpecificId, PurchaseFailureReason.UserCancelled, purchaseResultInfo.ErrMsg));
                        break;
                    }
                    default:
                    {
                        _storeEvents.OnPurchaseFailed(new PurchaseFailureDescription(product.storeSpecificId, PurchaseFailureReason.Unknown, purchaseResultInfo.ErrMsg));
                        break;
                    }
                }
            }, exception => { _storeEvents.OnPurchaseFailed(new PurchaseFailureDescription(product.id, PurchaseFailureReason.Unknown, exception.Message)); });
        }
    }
}