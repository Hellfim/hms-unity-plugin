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
        private List<ProductInfo> _productsList;
        private Dictionary<String, ProductInfo> _productsByID;
        private Dictionary<String, InAppPurchaseData> _purchasedData;

        private Boolean _isClientInitialized;
        private IIapClient _iapClient;
        private IStoreCallback _storeEvents;

        private ReadOnlyCollection<ProductDefinition> _initProductDefinitions;

        private List<ProductDefinition> ProductList = new List<ProductDefinition>();
        
        void IStore.Initialize(IStoreCallback callback)
        {
            _storeEvents = callback;

            _locker = new System.Object();
            _productsList = new List<ProductInfo>(100);
            _productsByID = new Dictionary<String, ProductInfo>(100);
            _purchasedData = new Dictionary<String, InAppPurchaseData>(50);
            
            InitializeClient();
        }

        void IStore.RetrieveProducts(ReadOnlyCollection<ProductDefinition> products)
        {
            lock (_locker)
            {
                Debug.Log("[HuaweiStore] IAP RetrieveProducts");
                foreach (var item in products)
                {
                    Debug.Log($"[HuaweiStore] Product Id: {item.id} ");
                    ProductList.Add(item);
                }

                _initProductDefinitions = products;

                if (_isClientInitialized)
                {
                    LoadConsumableProducts(ProductList);
                }
            }
        }

        void IStore.Purchase(ProductDefinition product, String developerPayload)
        {
            if (!_productsByID.ContainsKey(product.storeSpecificId))
            {
                _storeEvents.OnPurchaseFailed(new PurchaseFailureDescription(product.id, PurchaseFailureReason.ProductUnavailable, "UnknownProduct"));
                return;
            }

            var productInfo = _productsByID[product.storeSpecificId];
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
                if (_initProductDefinitions != null)
                {
                    LoadConsumableProducts(ProductList);
                }
            }
        }

        private void ProcessInitializationFailure(HMSException exception)
        {
            Debug.LogError("[HuaweiStore]: ERROR on ClientInitFailed: " + exception.WrappedCauseMessage + " " + exception.WrappedExceptionMessage);
            _storeEvents.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable);
        }

        private void LoadConsumableProducts(List<ProductDefinition> list)
        {
            ProductList = list;
            var consumablesIDs = list.Where(c => c.type == ProductType.Consumable).Select(c => c.storeSpecificId).ToList();
            CreateProductRequest(consumablesIDs, PriceType.IN_APP_CONSUMABLE, LoadNonConsumableProducts);
        }

        private void LoadNonConsumableProducts()
        {
            var nonConsumablesIDs = ProductList.Where(c => c.type == ProductType.NonConsumable).Select(c => c.storeSpecificId).ToList();
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
            var subscribeIDs = ProductList.Where(c => c.type == ProductType.Subscription).Select(c => c.storeSpecificId).ToList();
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
                      .AddOnSuccessListener(result => { ParseProducts(result, type); onSuccess(); });
        }

        private void GetProductsFailure(HMSException exception)
        {
            Debug.LogError("[HuaweiStore]: ERROR on GetProductsFailure: " + exception.WrappedCauseMessage + " " + exception.WrappedExceptionMessage);
            _storeEvents.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable);
        }

        private void ParseProducts(ProductInfoResult result, PriceType type)
        {
            if (result == null)
            {
                return;
            }

            if (result.ProductInfoList.Count == 0)
            {
                return;
            }

            foreach (var item in result.ProductInfoList)
            {
                Debug.Log($"[HuaweiStore]  Huawei Product Id: {item.ProductId}");
            }

            foreach (var productInfo in result.ProductInfoList)
            {
                _productsList.Add(productInfo);
                _productsByID.Add(productInfo.ProductId, productInfo);
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
            Debug.Log("ProductsLoaded");

            var descList = new List<ProductDescription>(_productsList.Count);

            foreach (var product in _productsList)
            {
                var price = product.MicrosPrice * 0.000001f;

                var priceString = price < 100 ? price.ToString("0.00") : ((Int32)(price + 0.5f)).ToString();

                priceString = product.Currency + " " + priceString;
                var prodMeta = new ProductMetadata(priceString, product.ProductName, product.ProductDesc, product.Currency, (Decimal)price);

                var prodDesc = _purchasedData.TryGetValue(product.ProductId, out var purchaseData) ? new ProductDescription(product.ProductId, prodMeta, CreateReceipt(purchaseData), purchaseData.OrderID) : new ProductDescription(product.ProductId, prodMeta);

                descList.Add(prodDesc);
            }

            _storeEvents.OnProductsRetrieved(descList);
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