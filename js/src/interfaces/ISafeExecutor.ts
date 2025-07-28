/**
 * Interface for Safe transaction execution
 */
export interface ISafeExecutor {
  /**
   * Executes a transaction through a Gnosis Safe
   * @param to Destination address
   * @param data Transaction data
   * @param value Value in wei to send (default: 0)
   * @param operation Call operation type (default: 0 for Call)
   * @param ct Cancellation token
   * @returns Transaction hash
   */
  execTransactionAsync(
    to: string,
    data: Uint8Array | string,
    value?: bigint,
    operation?: number,
    ct?: AbortSignal
  ): Promise<string>;
}
