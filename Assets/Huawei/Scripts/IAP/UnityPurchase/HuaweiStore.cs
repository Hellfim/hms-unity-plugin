﻿using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using HuaweiMobileServices.IAP;
using HuaweiMobileServices.Utils;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
using System.Linq;
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
        private HuaweiStoreProductsLoader _productsLoader;
        
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

                LoadProducts();
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
                          .AddOnFailureListener(exception => { Debug.LogError("Consume failed " + exception.Message + " " + exception.StackTrace); });
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
                
                LoadProducts();
            }
        }

        private void ProcessInitializationFailure(HMSException exception)
        {
            Debug.LogError("[HuaweiStore]: ERROR on ClientInitFailed: " + exception.WrappedCauseMessage + " " + exception.WrappedExceptionMessage);
            _storeEvents.OnSetupFailed(InitializationFailureReason.PurchasingUnavailable);
        }

        private void LoadProducts()
        {
            if (!_isClientInitialized || _productDefinitions == null || _productsLoader != null)
            {
                return;
            }

            _productsLoader = new HuaweiStoreProductsLoader(_iapClient, _storeEvents, _productsInfo, _purchasedData, _productDefinitions);
            _productsLoader.Start();
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