import type { Metadata } from 'next';

export const metadata: Metadata = {
  title: 'IronHive Web Chat',
  description: 'Sample web chat using ironhive CLI via subprocess',
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="ko">
      <body style={{ margin: 0, fontFamily: 'system-ui, sans-serif' }}>
        {children}
      </body>
    </html>
  );
}
