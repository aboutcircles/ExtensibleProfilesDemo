// Export interfaces
export * from './interfaces/INameRegistry';
export * from './interfaces/IIpfsStore';
export * from './interfaces/IProfileStore';
export * from './interfaces/ISignatureVerifier';
export * from './interfaces/ILinkSigner';
export * from './interfaces/IChainApi';
export * from './interfaces/ISafeExecutor';
export * from './interfaces/CustomDataLink';

// Export utility classes
export * from './Sha3';
export * from './CidConverter';
export * from './CanonicalJson';

// Export implementations
export * from './NameRegistry';
export * from './IpfsStore';
export * from './ProfileStore';
export * from './DefaultSignatureVerifier';
export * from './SafeLinkSigner';
export * from './GnosisSafeExecutor';
// Import polyfill first to ensure crypto is available
import './utils/crypto-polyfill';

// Core components
export { Sha3 } from './Sha3';
export { IpfsStore } from './IpfsStore';
export { CidConverter } from './CidConverter';
export { NameRegistry } from './NameRegistry';
export { ProfileStore } from './ProfileStore';
export { CanonicalJson } from './CanonicalJson';
export { SafeLinkSigner } from './SafeLinkSigner';
export { GnosisSafeExecutor } from './GnosisSafeExecutor';
export { DefaultSignatureVerifier } from './DefaultSignatureVerifier';

// New components
export { DefaultLinkSigner } from './DefaultLinkSigner';
export { EthereumChainApi } from './EthereumChainApi';
export { NamespaceWriter } from './NamespaceWriter';
export { DefaultNamespaceReader } from './DefaultNamespaceReader';
export { Helpers } from './utils/Helpers';

// Interfaces
export type { CustomDataLink } from './interfaces/CustomDataLink';
export type { NameIndexDoc } from './interfaces/NameIndexDoc';
export type { NamespaceChunk } from './interfaces/NamespaceChunk';
export type { Profile } from './interfaces/Profile';
export type { SigningKey } from './interfaces/SigningKey';
export type { IIpfsStore } from './interfaces/IIpfsStore';
export type { ILinkSigner } from './interfaces/ILinkSigner';
export type { INamespaceReader } from './interfaces/INamespaceReader';
export type { INamespaceWriter } from './interfaces/INamespaceWriter';
export type { ISignatureVerifier } from './interfaces/ISignatureVerifier';
export type { IChainApi } from './interfaces/IChainApi';
export type { SignatureCallResult } from './interfaces/SignatureCallResult';
// Export mocks for testing
export * from './mocks/NameRegistryMock';
