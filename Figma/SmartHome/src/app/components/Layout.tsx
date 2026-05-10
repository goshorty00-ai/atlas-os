import { motion, AnimatePresence } from 'motion/react';
import { Outlet } from 'react-router';
import { Sidebar } from './Sidebar';
import { VoiceAssistant } from './VoiceAssistant';
import { useEffect, useState } from 'react';
import { onHostMessage } from '../bridge';

export function Layout() {
  const [toast, setToast] = useState<{ msg: string; ok: boolean } | null>(null);

  useEffect(() => {
    const unsub = onHostMessage((type, payload) => {
      const p = payload as any;
      if (type === 'smart-home.actionResult') {
        setToast({ msg: p?.message ?? 'Done', ok: true });
        setTimeout(() => setToast(null), 3000);
      } else if (type === 'smart-home.error') {
        setToast({ msg: p?.message ?? 'Error', ok: false });
        setTimeout(() => setToast(null), 5000);
      }
    });
    return unsub;
  }, []);

  return (
    <div 
      className="min-h-screen w-full relative overflow-hidden"
      style={{
        background: '#050A12',
      }}
    >
      {/* Animated Background Effects */}
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        {/* Gradient Orbs */}
        <motion.div
          className="absolute top-1/4 left-1/4 w-96 h-96 rounded-full blur-3xl opacity-20"
          style={{
            background: 'radial-gradient(circle, #00d4ff 0%, transparent 70%)',
          }}
          animate={{
            scale: [1, 1.2, 1],
            opacity: [0.2, 0.3, 0.2],
          }}
          transition={{
            duration: 8,
            repeat: Infinity,
          }}
        />
        <motion.div
          className="absolute bottom-1/4 right-1/4 w-96 h-96 rounded-full blur-3xl opacity-20"
          style={{
            background: 'radial-gradient(circle, #0066ff 0%, transparent 70%)',
          }}
          animate={{
            scale: [1.2, 1, 1.2],
            opacity: [0.3, 0.2, 0.3],
          }}
          transition={{
            duration: 10,
            repeat: Infinity,
          }}
        />

        {/* Grid Background */}
        <div 
          className="absolute inset-0 opacity-10"
          style={{
            backgroundImage: 'linear-gradient(rgba(0, 212, 255, 0.1) 1px, transparent 1px), linear-gradient(90deg, rgba(0, 212, 255, 0.1) 1px, transparent 1px)',
            backgroundSize: '100px 100px',
          }}
        />

        {/* Floating Particles */}
        {[...Array(20)].map((_, i) => (
          <motion.div
            key={i}
            className="absolute w-1 h-1 rounded-full bg-cyan-400"
            style={{
              left: `${Math.random() * 100}%`,
              top: `${Math.random() * 100}%`,
            }}
            animate={{
              y: [0, -100, 0],
              opacity: [0, 1, 0],
            }}
            transition={{
              duration: 5 + Math.random() * 5,
              repeat: Infinity,
              delay: Math.random() * 5,
            }}
          />
        ))}
      </div>

      {/* Sidebar */}
      <Sidebar />

      {/* Voice Assistant - Fixed at top */}
      <div className="fixed top-0 left-20 right-0 z-40">
        <VoiceAssistant />
      </div>

      {/* Main Content */}
      <div className="pl-20 pt-20 h-screen overflow-y-auto">
        <div className="container mx-auto p-8 max-w-7xl">
          <Outlet />
          {/* Footer Spacing */}
          <div className="h-16" />
        </div>
      </div>

      {/* Scan Lines Effect */}
      <div className="fixed inset-0 pointer-events-none opacity-5">
        <motion.div
          className="absolute inset-x-0 h-1 bg-gradient-to-r from-transparent via-cyan-400 to-transparent"
          animate={{
            top: ['0%', '100%'],
          }}
          transition={{
            duration: 5,
            repeat: Infinity,
            ease: 'linear',
          }}
        />
      </div>

      {/* Toast Notifications */}
      <AnimatePresence>
        {toast && (
          <motion.div
            className="fixed bottom-6 right-6 z-50 px-5 py-3 rounded-xl text-sm font-medium backdrop-blur-xl"
            style={{
              background: toast.ok ? 'rgba(0,212,255,0.15)' : 'rgba(255,70,70,0.15)',
              border: `1px solid ${toast.ok ? 'rgba(0,212,255,0.5)' : 'rgba(255,70,70,0.5)'}`,
              color: toast.ok ? '#00d4ff' : '#ff4646',
              boxShadow: `0 0 20px ${toast.ok ? 'rgba(0,212,255,0.3)' : 'rgba(255,70,70,0.3)'}`,
              maxWidth: '400px',
            }}
            initial={{ opacity: 0, y: 20, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 10, scale: 0.95 }}
          >
            {toast.msg}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
