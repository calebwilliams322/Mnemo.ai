import { useState, useEffect } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { motion, AnimatePresence } from 'framer-motion';
import {
  FileText,
  ShieldCheck,
  MessageSquare,
  Zap,
  Upload,
  Search,
  FilePlus,
  Menu,
  X,
  ChevronRight,
  Sparkles,
  CheckCircle2,
  Lock,
} from 'lucide-react';
import { useAuthStore } from '../stores/authStore';

/**
 * PolicyCard - Background animation element
 */
const PolicyCard = ({ opacity = 0.1 }: { opacity?: number }) => (
  <div
    className="w-24 h-32 md:w-32 md:h-44 bg-white rounded-lg shadow-sm border border-gray-100 p-3 flex flex-col space-y-2 flex-shrink-0"
    style={{ opacity }}
  >
    <div className="w-8 h-1.5 bg-blue-100 rounded-full" />
    <div className="space-y-1">
      <div className="w-full h-1 bg-gray-50 rounded-full" />
      <div className="w-5/6 h-1 bg-gray-50 rounded-full" />
      <div className="w-full h-1 bg-gray-50 rounded-full" />
    </div>
    <div className="mt-auto flex justify-between items-end">
      <div className="w-6 h-6 rounded-full bg-blue-50" />
      <div className="w-10 h-2 bg-gray-50 rounded-full" />
    </div>
  </div>
);

/**
 * ConveyorBelt - Animated background
 */
const ConveyorBelt = () => {
  return (
    <div className="absolute inset-0 overflow-hidden pointer-events-none opacity-40">
      {/* Row 1 */}
      <div className="absolute top-[15%] -left-1/4 w-[150%] flex space-x-8">
        <motion.div
          className="flex space-x-8"
          animate={{ x: ['0%', '-50%'] }}
          transition={{ duration: 30, repeat: Infinity, ease: 'linear' }}
        >
          {[...Array(20)].map((_, i) => (
            <PolicyCard key={`row1-${i}`} />
          ))}
        </motion.div>
      </div>

      {/* Row 2 */}
      <div className="absolute top-[45%] -left-1/4 w-[150%] flex space-x-12">
        <motion.div
          className="flex space-x-12"
          animate={{ x: ['-10%', '-60%'] }}
          transition={{ duration: 45, repeat: Infinity, ease: 'linear' }}
        >
          {[...Array(20)].map((_, i) => (
            <PolicyCard key={`row2-${i}`} />
          ))}
        </motion.div>
      </div>

      {/* Row 3 */}
      <div className="absolute top-[75%] -left-1/4 w-[150%] flex space-x-10">
        <motion.div
          className="flex space-x-10"
          animate={{ x: ['-20%', '-70%'] }}
          transition={{ duration: 60, repeat: Infinity, ease: 'linear' }}
        >
          {[...Array(20)].map((_, i) => (
            <PolicyCard key={`row3-${i}`} />
          ))}
        </motion.div>
      </div>
    </div>
  );
};

/**
 * LandingNavbar - Navigation with Login/Get Started buttons
 */
const LandingNavbar = ({ isAuthenticated }: { isAuthenticated: boolean }) => {
  const [isScrolled, setIsScrolled] = useState(false);
  const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

  useEffect(() => {
    const handleScroll = () => setIsScrolled(window.scrollY > 20);
    window.addEventListener('scroll', handleScroll);
    return () => window.removeEventListener('scroll', handleScroll);
  }, []);

  const navLinks = [
    { name: 'Product', href: '#product' },
    { name: 'Features', href: '#features' },
    { name: 'How it Works', href: '#how-it-works' },
  ];

  const scrollToSection = (e: React.MouseEvent<HTMLAnchorElement>, href: string) => {
    e.preventDefault();
    const element = document.querySelector(href);
    if (element) {
      element.scrollIntoView({ behavior: 'smooth' });
    }
    setIsMobileMenuOpen(false);
  };

  return (
    <nav
      className={`fixed top-0 left-0 right-0 z-50 transition-all duration-300 ${
        isScrolled ? 'bg-white/80 backdrop-blur-lg border-b border-gray-100 py-3' : 'bg-transparent py-5'
      }`}
    >
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex justify-between items-center">
          <div className="flex items-center space-x-2">
            <div className="w-8 h-8 bg-blue-600 rounded-lg flex items-center justify-center">
              <Zap className="w-5 h-5 text-white fill-current" />
            </div>
            <span className="text-xl font-bold text-gray-900 tracking-tight">
              Mnemo<span className="text-blue-600">.ai</span>
            </span>
          </div>

          {/* Desktop Nav */}
          <div className="hidden md:flex items-center space-x-8">
            {navLinks.map((link) => (
              <a
                key={link.name}
                href={link.href}
                onClick={(e) => scrollToSection(e, link.href)}
                className="text-sm font-medium text-gray-600 hover:text-blue-600 transition-colors"
              >
                {link.name}
              </a>
            ))}
            {isAuthenticated ? (
              <Link
                to="/dashboard"
                className="bg-gray-900 text-white px-5 py-2 rounded-full text-sm font-medium hover:bg-gray-800 transition-all shadow-sm"
              >
                Back to App
              </Link>
            ) : (
              <>
                <Link
                  to="/login"
                  className="text-sm font-medium text-gray-600 hover:text-blue-600 transition-colors"
                >
                  Login
                </Link>
                <Link
                  to="/signup"
                  className="bg-gray-900 text-white px-5 py-2 rounded-full text-sm font-medium hover:bg-gray-800 transition-all shadow-sm"
                >
                  Get Started
                </Link>
              </>
            )}
          </div>

          {/* Mobile Toggle */}
          <div className="md:hidden">
            <button onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)} className="text-gray-600">
              {isMobileMenuOpen ? <X /> : <Menu />}
            </button>
          </div>
        </div>
      </div>

      {/* Mobile Menu */}
      <AnimatePresence>
        {isMobileMenuOpen && (
          <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            className="md:hidden bg-white border-b border-gray-100"
          >
            <div className="px-4 pt-2 pb-6 space-y-1">
              {navLinks.map((link) => (
                <a
                  key={link.name}
                  href={link.href}
                  onClick={(e) => scrollToSection(e, link.href)}
                  className="block px-3 py-4 text-base font-medium text-gray-700 hover:text-blue-600"
                >
                  {link.name}
                </a>
              ))}
              {isAuthenticated ? (
                <div className="px-3 pt-4">
                  <Link
                    to="/dashboard"
                    className="block w-full text-center bg-blue-600 text-white px-5 py-3 rounded-xl font-medium"
                    onClick={() => setIsMobileMenuOpen(false)}
                  >
                    Back to App
                  </Link>
                </div>
              ) : (
                <>
                  <Link
                    to="/login"
                    className="block px-3 py-4 text-base font-medium text-gray-700 hover:text-blue-600"
                    onClick={() => setIsMobileMenuOpen(false)}
                  >
                    Login
                  </Link>
                  <div className="px-3 pt-4">
                    <Link
                      to="/signup"
                      className="block w-full text-center bg-blue-600 text-white px-5 py-3 rounded-xl font-medium"
                      onClick={() => setIsMobileMenuOpen(false)}
                    >
                      Get Started
                    </Link>
                  </div>
                </>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </nav>
  );
};

/**
 * Hero Section
 */
const Hero = () => {
  return (
    <section className="relative pt-32 pb-20 lg:pt-48 lg:pb-32 overflow-hidden mesh-gradient">
      <ConveyorBelt />

      {/* Decorative Blobs */}
      <div className="absolute top-0 right-0 -translate-y-1/2 translate-x-1/4 w-[600px] h-[600px] bg-blue-100/40 rounded-full blur-3xl opacity-60"></div>
      <div className="absolute bottom-0 left-0 translate-y-1/2 -translate-x-1/4 w-[500px] h-[500px] bg-indigo-50/40 rounded-full blur-3xl opacity-60"></div>

      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 relative z-10">
        <div className="text-center max-w-4xl mx-auto">
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6 }}
            className="inline-flex items-center space-x-2 px-3 py-1 rounded-full bg-blue-50 border border-blue-100 text-blue-600 text-xs font-semibold uppercase tracking-wider mb-8"
          >
            <Sparkles className="w-3 h-3" />
            <span>Next-Gen Insurance Intelligence</span>
          </motion.div>

          <motion.h1
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, delay: 0.1 }}
            className="text-5xl lg:text-7xl font-bold text-gray-900 tracking-tight leading-[1.1] mb-6"
          >
            Understand any policy <br />
            <span className="text-transparent bg-clip-text bg-gradient-to-r from-blue-600 to-indigo-500">
              in seconds
            </span>
          </motion.h1>

          <motion.p
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, delay: 0.2 }}
            className="text-lg lg:text-xl text-gray-600 mb-10 max-w-2xl mx-auto leading-relaxed"
          >
            AI-powered summarization, gap analysis, and conversational insights designed specifically for
            independent agents and brokers.
          </motion.p>

          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.6, delay: 0.3 }}
            className="flex flex-col sm:flex-row items-center justify-center space-y-4 sm:space-y-0 sm:space-x-4"
          >
            <Link
              to="/signup"
              className="w-full sm:w-auto px-8 py-4 bg-blue-600 text-white rounded-full font-semibold hover:bg-blue-700 hover:shadow-lg hover:shadow-blue-200 transition-all flex items-center justify-center group"
            >
              Get Started
              <ChevronRight className="ml-2 w-4 h-4 group-hover:translate-x-1 transition-transform" />
            </Link>
            <Link
              to="/login"
              className="w-full sm:w-auto px-8 py-4 bg-white text-gray-900 border border-gray-200 rounded-full font-semibold hover:bg-gray-50 transition-all flex items-center justify-center"
            >
              Login
            </Link>
          </motion.div>
        </div>
      </div>
    </section>
  );
};

/**
 * Product Preview Section
 */
const ProductPreview = () => {
  return (
    <section id="product" className="py-20 bg-[#FAFAFA] relative overflow-hidden">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <motion.div
          initial={{ opacity: 0, scale: 0.95 }}
          whileInView={{ opacity: 1, scale: 1 }}
          viewport={{ once: true }}
          transition={{ duration: 0.8 }}
          className="relative mx-auto max-w-6xl group"
        >
          {/* Background Glow */}
          <div className="absolute -inset-4 bg-gradient-to-r from-blue-400/20 to-indigo-400/20 rounded-[2.5rem] blur-2xl transition duration-500 group-hover:opacity-100 opacity-60"></div>

          {/* Mockup Container */}
          <div className="relative glass-card rounded-[2rem] shadow-2xl overflow-hidden border border-white/40 ring-1 ring-black/5 rotate-1 md:rotate-2">
            <div className="bg-gray-900/5 h-10 border-b border-gray-100 flex items-center px-4 space-x-2">
              <div className="w-2.5 h-2.5 rounded-full bg-red-400"></div>
              <div className="w-2.5 h-2.5 rounded-full bg-yellow-400"></div>
              <div className="w-2.5 h-2.5 rounded-full bg-green-400"></div>
            </div>
            <div className="aspect-[16/9] md:aspect-auto md:h-[600px] bg-white p-4 md:p-8 flex gap-6 overflow-hidden">
              {/* Sidebar Simulation */}
              <div className="hidden md:block w-64 flex-shrink-0 bg-gray-50/50 rounded-xl border border-gray-100 p-4 space-y-4">
                <div className="h-8 w-3/4 bg-gray-200/50 rounded-md"></div>
                <div className="space-y-2">
                  {[1, 2, 3, 4].map((i) => (
                    <div key={i} className="h-4 w-full bg-gray-100 rounded"></div>
                  ))}
                </div>
                <div className="pt-8 space-y-2">
                  {[1, 2].map((i) => (
                    <div key={i} className="h-10 w-full bg-white border border-gray-100 rounded-lg"></div>
                  ))}
                </div>
              </div>
              {/* Main Content Simulation */}
              <div className="flex-1 space-y-6">
                <div className="flex justify-between items-center">
                  <div className="h-10 w-1/3 bg-gray-100 rounded-lg"></div>
                  <div className="h-10 w-1/4 bg-blue-50 rounded-lg border border-blue-100"></div>
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  <div className="h-48 bg-blue-600/5 rounded-2xl border border-blue-100 flex items-center justify-center flex-col p-6">
                    <div className="w-12 h-12 bg-white rounded-xl shadow-sm mb-4 flex items-center justify-center">
                      <FileText className="text-blue-600 w-6 h-6" />
                    </div>
                    <div className="h-4 w-32 bg-blue-200/50 rounded mb-2"></div>
                    <div className="h-3 w-48 bg-blue-100/50 rounded"></div>
                  </div>
                  <div className="h-48 bg-gray-50 rounded-2xl border border-gray-100 flex items-center justify-center flex-col p-6">
                    <div className="w-12 h-12 bg-white rounded-xl shadow-sm mb-4 flex items-center justify-center">
                      <ShieldCheck className="text-indigo-600 w-6 h-6" />
                    </div>
                    <div className="h-4 w-32 bg-blue-200/50 rounded mb-2"></div>
                    <div className="h-3 w-48 bg-gray-100/50 rounded"></div>
                  </div>
                </div>
                {/* Chat Bubble Simulation */}
                <div className="mt-auto pt-8 border-t border-gray-50 flex gap-4">
                  <div className="w-10 h-10 rounded-full bg-blue-600 flex-shrink-0 flex items-center justify-center">
                    <Zap className="w-5 h-5 text-white fill-current" />
                  </div>
                  <div className="bg-blue-50/50 border border-blue-100 p-4 rounded-2xl rounded-tl-none max-w-lg">
                    <p className="text-sm text-gray-700">
                      "Analyzing the GL policy... I found a potential coverage gap in the Cyber Liability
                      extension. Would you like to see a comparison?"
                    </p>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </motion.div>
      </div>
    </section>
  );
};

/**
 * Features Section
 */
const Features = () => {
  const features = [
    {
      title: 'Policy Summarization',
      desc: 'Upload any policy. Get instant, structured summaries powered by smart AI extraction.',
      icon: <FileText className="w-6 h-6" />,
      color: 'blue',
    },
    {
      title: 'Gap Analysis',
      desc: 'Compare coverage against industry standards. Spot gaps before they become claims.',
      icon: <ShieldCheck className="w-6 h-6" />,
      color: 'indigo',
    },
    {
      title: 'Proposal Generation',
      desc: 'Generate client-ready proposals in seconds, not hours. Professional and accurate.',
      icon: <FilePlus className="w-6 h-6" />,
      color: 'blue',
    },
    {
      title: 'RAG Chat',
      desc: 'Ask questions about any policy in plain English. Get accurate, cited answers from docs.',
      icon: <MessageSquare className="w-6 h-6" />,
      color: 'indigo',
    },
  ];

  return (
    <section id="features" className="py-24 bg-white">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="text-center mb-16">
          <h2 className="text-3xl font-bold text-gray-900 sm:text-4xl mb-4">Built for the modern broker</h2>
          <p className="text-lg text-gray-600 max-w-2xl mx-auto">
            Everything you need to handle complex policy reviews with unprecedented speed.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-8">
          {features.map((feature, idx) => (
            <motion.div
              key={idx}
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true }}
              transition={{ delay: idx * 0.1 }}
              className="p-8 rounded-3xl bg-gray-50 hover:bg-white hover:shadow-xl hover:shadow-blue-500/5 border border-transparent hover:border-blue-100 transition-all group"
            >
              <div
                className={`w-12 h-12 rounded-2xl bg-white shadow-sm flex items-center justify-center mb-6 group-hover:scale-110 transition-transform ${
                  feature.color === 'blue' ? 'text-blue-600' : 'text-indigo-600'
                }`}
              >
                {feature.icon}
              </div>
              <h3 className="text-xl font-bold text-gray-900 mb-3">{feature.title}</h3>
              <p className="text-gray-600 leading-relaxed text-sm">{feature.desc}</p>
            </motion.div>
          ))}
        </div>
      </div>
    </section>
  );
};

/**
 * How It Works Section
 */
const HowItWorks = () => {
  const steps = [
    {
      title: 'Upload',
      desc: 'Drag and drop your PDF or DOCX policies into the secure Mnemo portal.',
      icon: <Upload className="w-5 h-5" />,
    },
    {
      title: 'Analyze',
      desc: 'Our proprietary AI engine extracts keys data points and risk indicators instantly.',
      icon: <Search className="w-5 h-5" />,
    },
    {
      title: 'Deliver',
      desc: 'Review gap reports, chat with the policy, and export polished proposals.',
      icon: <CheckCircle2 className="w-5 h-5" />,
    },
  ];

  return (
    <section id="how-it-works" className="py-24 bg-[#FAFAFA]">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex flex-col lg:flex-row items-center gap-16">
          <div className="lg:w-1/2">
            <h2 className="text-4xl font-bold text-gray-900 mb-6 leading-tight">
              Insight-driven <br />
              workflow in minutes
            </h2>
            <p className="text-lg text-gray-600 mb-10">
              We've automated the tedious parts of policy review so you can focus on building relationships
              and winning business.
            </p>

            <div className="space-y-8">
              {steps.map((step, idx) => (
                <div key={idx} className="flex items-start space-x-4">
                  <div className="flex-shrink-0 w-10 h-10 rounded-full bg-blue-600 text-white flex items-center justify-center font-bold text-sm">
                    {idx + 1}
                  </div>
                  <div>
                    <h4 className="text-xl font-bold text-gray-900 mb-1">{step.title}</h4>
                    <p className="text-gray-600 text-sm leading-relaxed">{step.desc}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className="lg:w-1/2 relative">
            <div className="absolute -inset-4 bg-blue-400/10 blur-3xl rounded-full"></div>
            <div className="relative p-8 glass-card rounded-3xl border border-white">
              <div className="flex items-center justify-between mb-8">
                <div className="flex items-center space-x-3">
                  <div className="w-10 h-10 bg-blue-50 rounded-lg flex items-center justify-center text-blue-600">
                    <Upload className="w-5 h-5" />
                  </div>
                  <span className="font-semibold text-gray-800">Policy_Review_V2.pdf</span>
                </div>
                <span className="text-xs font-medium text-green-600 bg-green-50 px-2 py-1 rounded">
                  Processing... 92%
                </span>
              </div>
              <div className="space-y-4">
                <div className="h-4 bg-gray-100 rounded-full w-full"></div>
                <div className="h-4 bg-gray-100 rounded-full w-5/6"></div>
                <div className="h-4 bg-gray-100 rounded-full w-4/6"></div>
                <div className="flex gap-2 pt-4">
                  <div className="px-4 py-2 bg-blue-600 rounded-lg text-xs font-semibold text-white">
                    Generate Gap Report
                  </div>
                  <div className="px-4 py-2 bg-white border border-gray-200 rounded-lg text-xs font-semibold text-gray-700">
                    Open Chat
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
};

/**
 * CTA Section
 */
const CTASection = () => {
  const [email, setEmail] = useState('');
  const [isSubmitted, setIsSubmitted] = useState(false);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (email) {
      console.log('Capture email for demo:', email);
      setIsSubmitted(true);
      setEmail('');
    }
  };

  return (
    <section className="py-24 bg-white relative overflow-hidden">
      <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="bg-gray-900 rounded-[3rem] p-8 md:p-16 text-center relative overflow-hidden">
          {/* Background effects */}
          <div className="absolute top-0 right-0 -translate-y-1/2 translate-x-1/4 w-[400px] h-[400px] bg-blue-600/20 rounded-full blur-[100px]"></div>

          <div className="relative z-10">
            <h2 className="text-3xl md:text-5xl font-bold text-white mb-6">
              Ready to streamline <br />
              your workflow?
            </h2>
            <p className="text-gray-400 text-lg mb-10 max-w-xl mx-auto">
              Join hundreds of agencies already using Mnemo to win more deals and protect their clients
              better.
            </p>

            {!isSubmitted ? (
              <form onSubmit={handleSubmit} className="flex flex-col sm:flex-row gap-3 max-w-md mx-auto">
                <input
                  type="email"
                  placeholder="Enter your work email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="flex-1 px-6 py-4 rounded-2xl bg-white/10 border border-white/20 text-white placeholder:text-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 transition-all"
                  required
                />
                <button
                  type="submit"
                  className="px-8 py-4 bg-blue-600 text-white rounded-2xl font-bold hover:bg-blue-500 transition-all"
                >
                  Get Started
                </button>
              </form>
            ) : (
              <motion.div
                initial={{ opacity: 0, scale: 0.9 }}
                animate={{ opacity: 1, scale: 1 }}
                className="p-6 bg-blue-600/20 border border-blue-600/30 rounded-2xl inline-block"
              >
                <div className="flex items-center space-x-3 text-blue-400 font-semibold">
                  <CheckCircle2 className="w-6 h-6" />
                  <span>Thanks! We'll reach out shortly.</span>
                </div>
              </motion.div>
            )}

            <div className="mt-10 flex items-center justify-center space-x-6 text-sm text-gray-500">
              <div className="flex items-center space-x-2">
                <Lock className="w-4 h-4" />
                <span>Enterprise Secure</span>
              </div>
              <div className="flex items-center space-x-2">
                <CheckCircle2 className="w-4 h-4" />
                <span>HIPAA/SOC2 Compliant</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
};

/**
 * Footer
 */
const Footer = () => {
  return (
    <footer className="bg-white border-t border-gray-100 pt-20 pb-10">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="grid grid-cols-2 md:grid-cols-4 gap-12 mb-16">
          <div className="col-span-2 md:col-span-1">
            <div className="flex items-center space-x-2 mb-6">
              <div className="w-8 h-8 bg-blue-600 rounded-lg flex items-center justify-center">
                <Zap className="w-5 h-5 text-white fill-current" />
              </div>
              <span className="text-xl font-bold text-gray-900 tracking-tight">
                Mnemo<span className="text-blue-600">.ai</span>
              </span>
            </div>
            <p className="text-gray-500 text-sm leading-relaxed mb-6">
              Empowering insurance professionals with next-generation artificial intelligence.
            </p>
          </div>

          <div>
            <h4 className="font-bold text-gray-900 mb-6">Product</h4>
            <ul className="space-y-4 text-sm text-gray-500">
              <li>
                <a href="#features" className="hover:text-blue-600">
                  Summaries
                </a>
              </li>
              <li>
                <a href="#features" className="hover:text-blue-600">
                  Gap Analysis
                </a>
              </li>
              <li>
                <a href="#features" className="hover:text-blue-600">
                  Proposals
                </a>
              </li>
              <li>
                <a href="#" className="hover:text-blue-600">
                  Pricing
                </a>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="font-bold text-gray-900 mb-6">Company</h4>
            <ul className="space-y-4 text-sm text-gray-500">
              <li>
                <a href="#" className="hover:text-blue-600">
                  About Us
                </a>
              </li>
              <li>
                <a href="#" className="hover:text-blue-600">
                  Careers
                </a>
              </li>
              <li>
                <a href="#" className="hover:text-blue-600">
                  Blog
                </a>
              </li>
              <li>
                <a href="#" className="hover:text-blue-600">
                  Contact
                </a>
              </li>
            </ul>
          </div>

          <div>
            <h4 className="font-bold text-gray-900 mb-6">Legal</h4>
            <ul className="space-y-4 text-sm text-gray-500">
              <li>
                <a href="#" className="hover:text-blue-600">
                  Privacy Policy
                </a>
              </li>
              <li>
                <a href="#" className="hover:text-blue-600">
                  Terms of Service
                </a>
              </li>
              <li>
                <a href="#" className="hover:text-blue-600">
                  Security
                </a>
              </li>
            </ul>
          </div>
        </div>

        <div className="pt-8 border-t border-gray-100 flex flex-col md:flex-row justify-between items-center text-sm text-gray-400">
          <p>&copy; {new Date().getFullYear()} Mnemo.ai. All rights reserved.</p>
          <div className="flex space-x-6 mt-4 md:mt-0">
            <a href="#" className="hover:text-blue-600">
              LinkedIn
            </a>
            <a href="#" className="hover:text-blue-600">
              Twitter
            </a>
          </div>
        </div>
      </div>
    </footer>
  );
};

/**
 * Main Landing Page Component
 */
export function LandingPage({ skipAuthRedirect = false }: { skipAuthRedirect?: boolean }) {
  const { isAuthenticated, isLoading } = useAuthStore();

  // Redirect authenticated users to dashboard (unless viewing as "About" page)
  if (!skipAuthRedirect && !isLoading && isAuthenticated) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <div className="min-h-screen bg-[#FAFAFA] text-gray-900 selection:bg-blue-100 selection:text-blue-900">
      <LandingNavbar isAuthenticated={isAuthenticated} />
      <main>
        <Hero />
        <ProductPreview />
        <Features />
        <HowItWorks />
        <CTASection />
      </main>
      <Footer />
    </div>
  );
}
