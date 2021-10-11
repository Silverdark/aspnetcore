// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Cryptography.Cng;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.AspNetCore.DataProtection
{
    /// <summary>
    /// An <see cref="IDataProtectionProvider"/> that is transient.
    /// </summary>
    /// <remarks>
    /// Payloads generated by a given <see cref="EphemeralDataProtectionProvider"/> instance can only
    /// be deciphered by that same instance. Once the instance is lost, all ciphertexts
    /// generated by that instance are permanently undecipherable.
    /// </remarks>
    public sealed class EphemeralDataProtectionProvider : IDataProtectionProvider
    {
        private readonly KeyRingBasedDataProtectionProvider _dataProtectionProvider;

        /// <summary>
        /// Creates an ephemeral <see cref="IDataProtectionProvider"/>.
        /// </summary>
        public EphemeralDataProtectionProvider()
            : this (NullLoggerFactory.Instance)
        { }

        /// <summary>
        /// Creates an ephemeral <see cref="IDataProtectionProvider"/> with logging.
        /// </summary>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory" />.</param>
        public EphemeralDataProtectionProvider(ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            IKeyRingProvider keyringProvider;
            if (OSVersionUtil.IsWindows())
            {
                // Assertion for platform compat analyzer
                Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));
                // Fastest implementation: AES-256-GCM [CNG]
                keyringProvider = new EphemeralKeyRing<CngGcmAuthenticatedEncryptorConfiguration>(loggerFactory);
            }
            else
            {
                // Slowest implementation: AES-256-CBC + HMACSHA256 [Managed]
                keyringProvider = new EphemeralKeyRing<ManagedAuthenticatedEncryptorConfiguration>(loggerFactory);
            }

            var logger = loggerFactory.CreateLogger<EphemeralDataProtectionProvider>();
            logger.UsingEphemeralDataProtectionProvider();

            _dataProtectionProvider = new KeyRingBasedDataProtectionProvider(keyringProvider, loggerFactory);
        }

        /// <inheritdoc />
        public IDataProtector CreateProtector(string purpose)
        {
            if (purpose == null)
            {
                throw new ArgumentNullException(nameof(purpose));
            }

            // just forward to the underlying provider
            return _dataProtectionProvider.CreateProtector(purpose);
        }

        private sealed class EphemeralKeyRing<T> : IKeyRing, IKeyRingProvider
            where T : AlgorithmConfiguration, new()
        {
            public EphemeralKeyRing(ILoggerFactory loggerFactory)
            {
                DefaultAuthenticatedEncryptor = GetDefaultEncryptor(loggerFactory);
            }

            public IAuthenticatedEncryptor? DefaultAuthenticatedEncryptor { get; }

            public Guid DefaultKeyId { get; }

            public IAuthenticatedEncryptor? GetAuthenticatedEncryptorByKeyId(Guid keyId, out bool isRevoked)
            {
                isRevoked = false;
                return (keyId == default(Guid)) ? DefaultAuthenticatedEncryptor : null;
            }

            public IKeyRing GetCurrentKeyRing()
            {
                return this;
            }

            private static IAuthenticatedEncryptor? GetDefaultEncryptor(ILoggerFactory loggerFactory)
            {
                var configuration = new T();
                if (configuration is CngGcmAuthenticatedEncryptorConfiguration cngConfiguration)
                {
                    Debug.Assert(RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

                    var descriptor = (CngGcmAuthenticatedEncryptorDescriptor)new T().CreateNewDescriptor();
                    return new CngGcmAuthenticatedEncryptorFactory(loggerFactory)
                        .CreateAuthenticatedEncryptorInstance(
                            descriptor.MasterKey,
                            cngConfiguration);
                }
                else if (configuration is ManagedAuthenticatedEncryptorConfiguration managedConfiguration)
                {
                    var descriptor = (ManagedAuthenticatedEncryptorDescriptor)new T().CreateNewDescriptor();
                    return new ManagedAuthenticatedEncryptorFactory(loggerFactory)
                        .CreateAuthenticatedEncryptorInstance(
                            descriptor.MasterKey,
                            managedConfiguration);
                }

                return null;
            }
        }
    }
}
