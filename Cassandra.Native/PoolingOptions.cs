using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Cassandra.Native
{
    /**
     * Options related to connection pooling.
     * <p>
     * The driver uses connections in an asynchronous way. Meaning that
     * multiple requests can be submitted on the same connection at the same
     * time. This means that the driver only needs to maintain a relatively
     * small number of connections to each Cassandra host. These options allow
     * to control how many connections are kept exactly.
     * <p>
     * For each host, the driver keeps a core amount of connections open at all
     * time ({@link PoolingOptions#getCoreConnectionsPerHost}). If the
     * utilisation of those connections reaches a configurable threshold
     * ({@link PoolingOptions#getMaxSimultaneousRequestsPerConnectionTreshold}),
     * more connections are created up to a configurable maximum number of
     * connections ({@link PoolingOptions#getMaxConnectionPerHost}). Once more
     * than core connections have been created, connections in excess are
     * reclaimed if the utilisation of opened connections drops below the
     * configured threshold ({@link PoolingOptions#getMinSimultaneousRequestsPerConnectionTreshold}).
     * <p>
     * Each of these parameters can be separately set for {@code LOCAL} and
     * {@code REMOTE} hosts ({@link HostDistance}). For {@code IGNORED} hosts,
     * the default for all those settings is 0 and cannot be changed.
     */
    public class PoolingOptions
    {

        // Note: we could use an enumMap or similar, but synchronization would
        // be more costly so let's stick to volatile in for now.
        private static readonly int DEFAULT_MIN_REQUESTS = 25;
        private static readonly int DEFAULT_MAX_REQUESTS = 100;

        private static readonly int DEFAULT_CORE_POOL_LOCAL = 2;
        private static readonly int DEFAULT_CORE_POOL_REMOTE = 1;

        private static readonly int DEFAULT_MAX_POOL_LOCAL = 8;
        private static readonly int DEFAULT_MAX_POOL_REMOTE = 2;

        private volatile int minSimultaneousRequestsForLocal = DEFAULT_MIN_REQUESTS;
        private volatile int minSimultaneousRequestsForRemote = DEFAULT_MIN_REQUESTS;

        private volatile int maxSimultaneousRequestsForLocal = DEFAULT_MAX_REQUESTS;
        private volatile int maxSimultaneousRequestsForRemote = DEFAULT_MAX_REQUESTS;

        private volatile int coreConnectionsForLocal = DEFAULT_CORE_POOL_LOCAL;
        private volatile int coreConnectionsForRemote = DEFAULT_CORE_POOL_REMOTE;

        private volatile int maxConnectionsForLocal = DEFAULT_MAX_POOL_LOCAL;
        private volatile int maxConnectionsForRemote = DEFAULT_MAX_POOL_REMOTE;

        public PoolingOptions() { }

        /**
         * Number of simultaneous requests on a connection below which
         * connections in excess are reclaimed.
         * <p>
         * If an opened connection to an host at distance {@code distance}
         * handles less than this number of simultaneous requests and there is
         * more than {@link #getCoreConnectionsPerHost} connections open to this
         * host, the connection is closed.
         * <p>
         * The default value for this option is 25 for {@code LOCAL} and
         * {@code REMOTE} hosts.
         *
         * @param distance the {@code HostDistance} for which to return this threshold.
         * @return the configured threshold, or the default one if none have been set.
         */
        public int GetMinSimultaneousRequestsPerConnectionTreshold(HostDistance distance)
        {
            switch (distance)
            {
                case HostDistance.LOCAL:
                    return minSimultaneousRequestsForLocal;
                case HostDistance.REMOTE:
                    return minSimultaneousRequestsForRemote;
                default:
                    return 0;
            }
        }

        /**
         * Sets the number of simultaneous requests on a connection below which
         * connections in excess are reclaimed.
         *
         * @param distance the {@code HostDistance} for which to configure this threshold.
         * @param minSimultaneousRequests the value to set.
         * @return this {@code PoolingOptions}.
         *
         * @throws IllegalArgumentException if {@code distance == HostDistance.IGNORED}.
         */
        public PoolingOptions SetMinSimultaneousRequestsPerConnectionTreshold(HostDistance distance, int minSimultaneousRequests)
        {
            switch (distance)
            {
                case HostDistance.LOCAL:
                    minSimultaneousRequestsForLocal = minSimultaneousRequests;
                    break;
                case HostDistance.REMOTE:
                    minSimultaneousRequestsForRemote = minSimultaneousRequests;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Cannot set min streams per connection threshold for " + distance + " hosts");
            }
            return this;
        }

        /**
         * Number of simultaneous requests on all connections to an host after
         * which more connections are created.
         * <p>
         * If all the connections opened to an host at distance {@code
         * distance} connection are handling more than this number of
         * simultaneous requests and there is less than
         * {@link #getMaxConnectionPerHost} connections open to this host, a
         * new connection is open.
         * <p>
         * Note that a given connection cannot handle more than 128
         * simultaneous requests (protocol limitation).
         * <p>
         * The default value for this option is 100 for {@code LOCAL} and
         * {@code REMOTE} hosts.
         *
         * @param distance the {@code HostDistance} for which to return this threshold.
         * @return the configured threshold, or the default one if none have been set.
         */
        public int GetMaxSimultaneousRequestsPerConnectionTreshold(HostDistance distance)
        {
            switch (distance)
            {
                case HostDistance.LOCAL:
                    return maxSimultaneousRequestsForLocal;
                case HostDistance.REMOTE:
                    return maxSimultaneousRequestsForRemote;
                default:
                    return 0;
            }
        }

        /**
         * Sets number of simultaneous requests on all connections to an host after
         * which more connections are created.
         *
         * @param distance the {@code HostDistance} for which to configure this threshold.
         * @param maxSimultaneousRequests the value to set.
         * @return this {@code PoolingOptions}.
         *
         * @throws IllegalArgumentException if {@code distance == HostDistance.IGNORED}.
         */
        public PoolingOptions SetMaxSimultaneousRequestsPerConnectionTreshold(HostDistance distance, int maxSimultaneousRequests)
        {
            switch (distance)
            {
                case HostDistance.LOCAL:
                    maxSimultaneousRequestsForLocal = maxSimultaneousRequests;
                    break;
                case HostDistance.REMOTE:
                    maxSimultaneousRequestsForRemote = maxSimultaneousRequests;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Cannot set max streams per connection threshold for " + distance + " hosts");
            }
            return this;
        }

        /**
         * The core number of connections per host.
         * <p>
         * For the provided {@code distance}, this correspond to the number of
         * connections initially created and kept open to each host of that
         * distance.
         *
         * @param distance the {@code HostDistance} for which to return this threshold.
         * @return the core number of connections per host at distance {@code distance}.
         */
        public int GetCoreConnectionsPerHost(HostDistance distance)
        {
            switch (distance)
            {
                case HostDistance.LOCAL:
                    return coreConnectionsForLocal;
                case HostDistance.REMOTE:
                    return coreConnectionsForRemote;
                default:
                    return 0;
            }
        }

        /**
         * Sets the core number of connections per host.
         *
         * @param distance the {@code HostDistance} for which to set this threshold.
         * @param coreConnections the value to set
         * @return this {@code PoolingOptions}.
         *
         * @throws IllegalArgumentException if {@code distance == HostDistance.IGNORED}.
         */
        public PoolingOptions SetCoreConnectionsPerHost(HostDistance distance, int coreConnections)
        {
            switch (distance)
            {
                case HostDistance.LOCAL:
                    coreConnectionsForLocal = coreConnections;
                    break;
                case HostDistance.REMOTE:
                    coreConnectionsForRemote = coreConnections;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Cannot set core connections per host for " + distance + " hosts");
            }
            return this;
        }

        /**
         * The maximum number of connections per host.
         * <p>
         * For the provided {@code distance}, this correspond to the maximum
         * number of connections that can be created per host at that distance.
         *
         * @param distance the {@code HostDistance} for which to return this threshold.
         * @return the maximum number of connections per host at distance {@code distance}.
         */
        public int GetMaxConnectionPerHost(HostDistance distance)
        {
            switch (distance)
            {
                case HostDistance.LOCAL:
                    return maxConnectionsForLocal;
                case HostDistance.REMOTE:
                    return maxConnectionsForRemote;
                default:
                    return 0;
            }
        }

        /**
         * Sets the maximum number of connections per host.
         *
         * @param distance the {@code HostDistance} for which to set this threshold.
         * @param maxConnections the value to set
         * @return this {@code PoolingOptions}.
         *
         * @throws IllegalArgumentException if {@code distance == HostDistance.IGNORED}.
         */
        public PoolingOptions SetMaxConnectionsPerHost(HostDistance distance, int maxConnections)
        {
            switch (distance)
            {
                case HostDistance.LOCAL:
                    maxConnectionsForLocal = maxConnections;
                    break;
                case HostDistance.REMOTE:
                    maxConnectionsForRemote = maxConnections;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Cannot set max connections per host for " + distance + " hosts");
            }
            return this;
        }
    }
}