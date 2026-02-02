/** @type {import('next').NextConfig} */
const nextConfig = {
  // Allow streaming responses
  experimental: {
    serverActions: {
      bodySizeLimit: '2mb',
    },
  },
};

module.exports = nextConfig;
