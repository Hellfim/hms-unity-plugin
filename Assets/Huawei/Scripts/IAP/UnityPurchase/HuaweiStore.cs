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

        public static String PurchaseTokenOfLastPurchase = null;

        public static HuaweiStore GetInstance()
        {
            if (_currentInstance != null)
            {
                return _currentInstance;
            }

            _currentInstance = new HuaweiStore();
            return _currentInstance;
        }

        private IStoreCallback _storeEvents;

        void IStore.Initialize(IStoreCallback callback)
        {
            _storeEvents = callback;

            BaseInit();
            CreateClient();
        }

        private System.Object _locker;
        private List<ProductInfo> _productsList;
        private Dictionary<String, ProductInfo> _productsByID;
        private Dictionary<String, InAppPurchaseData> _purchasedData;
        public static ProductDefinition CurrentProduct;

        public void BaseInit()
        {
            _locker = new System.Object();
            _productsList = new List<ProductInfo>(100);
            _productsByID = new Dictionary<String, ProductInfo>(100);
            _purchasedData = new Dictionary<String, InAppPurchaseData>(50);
        }

        private IIapClient _iapClient;

        public void CreateClient()
        {
            _iapClient = Iap.GetIapClient();
            Debug.Log("[HuaweiStore] IAP Client Created");
            var moduleInitTask = _iapClient.EnvReady;

            moduleInitTask.AddOnSuccessListener(ClientinitSuccess).AddOnFailureListener(ClientInitFailed);
        }


        private Boolean _clientInited = false;

        public void ClientinitSuccess(EnvReadyResult result)
        {
            Debug.Log("[HuaweiStore] IAP Client Success");
            lock (_locker)
            {
                _clientInited = true;
                if (_initProductDefinitions != null)
                {
                    LoadConsumableProducts(ProductList);
                }
            }
        }

        public void ClientInitFailed(HMSException exception)
        {
            Debug.LogError("[HuaweiStore]: ERROR on ClientInitFailed: " + exception.WrappedCauseMessage + " " + exception.WrappedExceptionMessage);
            _storeEvents.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable);
        }

        private ReadOnlyCollection<ProductDefinition> _initProductDefinitions;

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

                if (_clientInited)
                {
                    LoadConsumableProducts(ProductList);
                }
            }
        }

        public List<ProductDefinition> ProductList = new List<ProductDefinition>();

        public void LoadConsumableProducts(List<ProductDefinition> list)
        {
            ProductList = list;
            var consumablesIDs = list.Where(c => c.type == ProductType.Consumable).Select(c => c.storeSpecificId).ToList();
            CreateProductRequest(consumablesIDs, PriceType.IN_APP_CONSUMABLE, LoadNonConsumableProducts);
        }

        public void LoadNonConsumableProducts()
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

        public void LoadSubscribeProducts()
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

        public void CreateProductRequest(List<String> consumablesIDs, PriceType type, Action onSuccess)
        {
            var productsDataRequest = new ProductInfoReq();
            productsDataRequest.PriceType = type;
            productsDataRequest.ProductIds = consumablesIDs;

            var task = _iapClient.ObtainProductInfo(productsDataRequest);
            task.AddOnFailureListener(GetProductsFailure);
            task.AddOnSuccessListener(result =>
            {
                ParseProducts(result, type);
                onSuccess();
            });
        }

        public void GetProductsFailure(HMSException exception)
        {
            Debug.LogError("[HuaweiStore]: ERROR on GetProductsFailure: " + exception.WrappedCauseMessage + " " + exception.WrappedExceptionMessage);
            _storeEvents.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable);
        }

        public void ParseProducts(ProductInfoResult result, PriceType type)
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

        public void LoadOwnedConsumables()
        {
            CreateOwnedPurchaseRequest(PriceType.IN_APP_CONSUMABLE, LoadOwnedNonConsumables);
        }

        public void LoadOwnedNonConsumables()
        {
            CreateOwnedPurchaseRequest(PriceType.IN_APP_NONCONSUMABLE, LoadOwnedSubscribes);
        }

        public void LoadOwnedSubscribes()
        {
            CreateOwnedPurchaseRequest(PriceType.IN_APP_SUBSCRIPTION, ProductsLoaded);
        }

        public void CreateOwnedPurchaseRequest(PriceType type, Action onSuccess)
        {
            var ownedPurchasesReq = new OwnedPurchasesReq();
            ownedPurchasesReq.PriceType = type;

            var task = _iapClient.ObtainOwnedPurchases(ownedPurchasesReq);

            task.AddOnSuccessListener(result =>
            {
                ParseOwned(result);
                onSuccess();
            });
        }

        public void ParseOwned(OwnedPurchasesResult result)
        {
            if (result == null || result.InAppPurchaseDataList == null)
            {
                return;
            }

            foreach (var inAppPurchaseData in result.InAppPurchaseDataList)
            {
                _purchasedData[inAppPurchaseData.ProductId] = inAppPurchaseData;

                Debug.Log($"ProductId: " + inAppPurchaseData.ProductId + " , ProductName: " + inAppPurchaseData.ProductName);
            }
        }

        public void ProductsLoaded()
        {
            Debug.Log("ProductsLoaded");

            var descList = new List<ProductDescription>(_productsList.Count);

            foreach (var product in _productsList)
            {
                String priceString;
                var price = product.MicrosPrice * 0.000001f;

                if (price < 100)
                {
                    priceString = price.ToString("0.00");
                }
                else
                {
                    priceString = ((Int32)(price + 0.5f)).ToString();
                }

                priceString = product.Currency + " " + priceString;
                var prodMeta = new ProductMetadata(priceString, product.ProductName, product.ProductDesc, product.Currency, (Decimal)price);

                var prodDesc = _purchasedData.TryGetValue(product.ProductId, out var purchaseData) ? new ProductDescription(product.ProductId, prodMeta, CreateReceipt(purchaseData), purchaseData.OrderID) : new ProductDescription(product.ProductId, prodMeta);

                descList.Add(prodDesc);
            }

            _storeEvents.OnProductsRetrieved(descList);
        }

        public static String CreateReceipt(InAppPurchaseData purchaseData)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{').Append("\"Store\":\"AppGallery\",\"TransactionID\":\"").Append(purchaseData.OrderID).Append("\", \"Payload\":{ ");
            sb.Append("\"product\":\"").Append(purchaseData.ProductId).Append("\"");
            sb.Append('}');
            sb.Append('}');
            return sb.ToString();
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

        public void PurchaseIntentCreated(PurchaseIntentResult intentResult, ProductDefinition product)
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
                        PurchaseTokenOfLastPurchase = purchaseResultInfo.InAppPurchaseData.PurchaseToken;

                        CurrentProduct = product;
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

        void IStore.FinishTransaction(ProductDefinition product, String transactionId)
        {
            if (_purchasedData.TryGetValue(product.storeSpecificId, out var data))
            {
                var token = data.PurchaseToken;
                var request = new ConsumeOwnedPurchaseReq();
                request.PurchaseToken = token;

                var task = _iapClient.ConsumeOwnedPurchase(request);
                task.AddOnSuccessListener(_ => { _purchasedData.Remove(product.storeSpecificId); });

                task.AddOnFailureListener(exception => { Debug.Log("Consume failed " + exception.Message + " " + exception.StackTrace); });
            }
        }
    }
}