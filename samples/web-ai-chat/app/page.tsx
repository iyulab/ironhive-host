'use client';

import { useState, useRef, useEffect } from 'react';

interface Message {
  role: 'user' | 'assistant';
  content: string;
}

interface Gap {
  id: string;
  description: string;
  priority: string;
}

export default function Home() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [gaps, setGaps] = useState<Gap[]>([]);
  const [showGaps, setShowGaps] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    // Fetch discovered gaps
    fetch('/api/gaps')
      .then((res) => res.json())
      .then((data) => setGaps(data.gaps || []))
      .catch(console.error);
  }, []);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  const sendMessage = async () => {
    if (!input.trim() || isLoading) return;

    const userMessage = input.trim();
    setInput('');
    setMessages((prev) => [...prev, { role: 'user', content: userMessage }]);
    setIsLoading(true);

    // Add placeholder for assistant
    setMessages((prev) => [...prev, { role: 'assistant', content: '' }]);

    try {
      const response = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ prompt: userMessage }),
      });

      if (!response.body) throw new Error('No response body');

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let fullContent = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value, { stream: true });
        fullContent += chunk;

        setMessages((prev) => {
          const updated = [...prev];
          updated[updated.length - 1] = { role: 'assistant', content: fullContent };
          return updated;
        });
      }
    } catch (error) {
      setMessages((prev) => {
        const updated = [...prev];
        updated[updated.length - 1] = {
          role: 'assistant',
          content: `Error: ${error}`,
        };
        return updated;
      });
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div style={styles.container}>
      <header style={styles.header}>
        <h1 style={styles.title}>IronHive Web Chat</h1>
        <span style={styles.subtitle}>CLI subprocess integration sample</span>
        <button
          style={styles.gapButton}
          onClick={() => setShowGaps(!showGaps)}
        >
          Gaps ({gaps.length})
        </button>
      </header>

      {showGaps && (
        <div style={styles.gapPanel}>
          <h3>Discovered CLI Gaps</h3>
          <ul>
            {gaps.map((gap) => (
              <li key={gap.id} style={styles.gapItem}>
                <strong>[{gap.id}]</strong> {gap.description}
                <span style={{ ...styles.priority, ...getPriorityStyle(gap.priority) }}>
                  {gap.priority}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}

      <main style={styles.chatBox}>
        {messages.length === 0 && (
          <div style={styles.placeholder}>
            ironhive CLI를 통해 대화를 시작하세요.
            <br />
            <small>CLI가 설치되어 있어야 합니다: dotnet tool install -g IronHive.Cli</small>
          </div>
        )}
        {messages.map((msg, i) => (
          <div
            key={i}
            style={{
              ...styles.message,
              ...(msg.role === 'user' ? styles.userMessage : styles.assistantMessage),
            }}
          >
            <div style={styles.role}>{msg.role === 'user' ? 'You' : 'IronHive'}</div>
            <div style={styles.content}>{msg.content || '...'}</div>
          </div>
        ))}
        <div ref={messagesEndRef} />
      </main>

      <footer style={styles.inputArea}>
        <input
          type="text"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && sendMessage()}
          placeholder="메시지 입력..."
          style={styles.input}
          disabled={isLoading}
        />
        <button onClick={sendMessage} style={styles.sendButton} disabled={isLoading}>
          {isLoading ? '...' : '전송'}
        </button>
      </footer>
    </div>
  );
}

const getPriorityStyle = (priority: string) => {
  switch (priority) {
    case 'high':
      return { background: '#ff4444', color: 'white' };
    case 'medium':
      return { background: '#ffaa00', color: 'black' };
    default:
      return { background: '#888', color: 'white' };
  }
};

const styles: Record<string, React.CSSProperties> = {
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100vh',
    background: '#1a1a2e',
    color: '#eee',
  },
  header: {
    padding: '15px 20px',
    background: '#16213e',
    display: 'flex',
    alignItems: 'center',
    gap: '15px',
  },
  title: {
    margin: 0,
    fontSize: '1.3rem',
    color: '#0f0',
  },
  subtitle: {
    fontSize: '0.8rem',
    color: '#888',
    flex: 1,
  },
  gapButton: {
    padding: '5px 15px',
    background: '#0f4c75',
    border: 'none',
    borderRadius: '4px',
    color: 'white',
    cursor: 'pointer',
  },
  gapPanel: {
    background: '#0d1b2a',
    padding: '15px 20px',
    borderBottom: '1px solid #333',
    maxHeight: '200px',
    overflowY: 'auto',
  },
  gapItem: {
    marginBottom: '8px',
    fontSize: '0.9rem',
  },
  priority: {
    marginLeft: '10px',
    padding: '2px 8px',
    borderRadius: '3px',
    fontSize: '0.75rem',
  },
  chatBox: {
    flex: 1,
    padding: '20px',
    overflowY: 'auto',
  },
  placeholder: {
    textAlign: 'center',
    color: '#666',
    marginTop: '100px',
  },
  message: {
    marginBottom: '15px',
    padding: '12px',
    borderRadius: '8px',
  },
  userMessage: {
    background: '#0f4c75',
    marginLeft: '20%',
  },
  assistantMessage: {
    background: '#1b262c',
    marginRight: '20%',
  },
  role: {
    fontSize: '0.75rem',
    color: '#888',
    marginBottom: '5px',
  },
  content: {
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  inputArea: {
    display: 'flex',
    gap: '10px',
    padding: '15px 20px',
    background: '#16213e',
  },
  input: {
    flex: 1,
    padding: '12px',
    border: 'none',
    borderRadius: '6px',
    background: '#1a1a2e',
    color: '#eee',
    fontSize: '1rem',
  },
  sendButton: {
    padding: '12px 24px',
    border: 'none',
    borderRadius: '6px',
    background: '#0f0',
    color: '#000',
    fontWeight: 'bold',
    cursor: 'pointer',
  },
};
