import { useState } from "react";
import { Plus, Key, Lock, Code } from "lucide-react";
import { postToHost } from "../atlasBridge";

type AuthMethod = "oauth" | "token" | "apikey";

export function AddIntegrationPanel() {
  const [selectedAuth, setSelectedAuth] =
    useState<AuthMethod>("oauth");
  const [name, setName] = useState("");
  const [clientId, setClientId] = useState("");
  const [clientSecret, setClientSecret] = useState("");
  const [tokenOrKey, setTokenOrKey] = useState("");
  const [endpointUrl, setEndpointUrl] = useState("");

  const authMethods = [
    {
      id: "oauth" as AuthMethod,
      icon: Lock,
      label: "OAuth 2.0",
      color: "blue",
    },
    {
      id: "token" as AuthMethod,
      icon: Code,
      label: "Bearer Token",
      color: "violet",
    },
    {
      id: "apikey" as AuthMethod,
      icon: Key,
      label: "API Key",
      color: "cyan",
    },
  ];

  return (
    <div
      className="relative rounded-xl border border-blue-500/20 bg-gradient-to-br from-blue-500/5 to-violet-500/5 backdrop-blur-xl p-6"
      style={{
        boxShadow: "0 8px 32px 0 rgba(31, 38, 135, 0.15)",
      }}
    >
      {/* Glassmorphism overlay */}
      <div className="absolute inset-0 rounded-xl bg-gradient-to-br from-white/5 to-white/0 pointer-events-none" />

      <div className="relative space-y-5">
        <div className="flex items-center justify-between">
          <h3 className="text-lg font-medium text-gray-200">
            Add Integration
          </h3>
          <Plus className="w-5 h-5 text-blue-400" />
        </div>

        {/* Integration name input */}
        <div className="space-y-2">
          <label className="text-sm text-gray-400">
            Integration Name
          </label>
          <input
            type="text"
            placeholder="Enter service name..."
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="w-full h-10 px-4 bg-gray-900/50 border border-blue-500/20 rounded-lg text-sm text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-400/50 focus:bg-gray-900/70 transition-all"
          />
        </div>

        {/* Divider */}
        <div className="h-[1px] bg-gradient-to-r from-transparent via-blue-500/30 to-transparent" />

        {/* Authentication method */}
        <div className="space-y-3">
          <label className="text-sm text-gray-400">
            Authentication Method
          </label>

          <div className="grid grid-cols-3 gap-2">
            {authMethods.map((method) => {
              const Icon = method.icon;
              const isSelected = selectedAuth === method.id;

              return (
                <button
                  key={method.id}
                  onClick={() => setSelectedAuth(method.id)}
                  className={`
                    relative p-3 rounded-lg border transition-all group
                    ${
                      isSelected
                        ? `border-${method.color}-400/50 bg-${method.color}-500/10`
                        : "border-blue-500/20 bg-gray-900/30 hover:border-blue-400/30"
                    }
                  `}
                >
                  <div className="flex flex-col items-center space-y-2">
                    <Icon
                      className={`w-5 h-5 ${
                        isSelected
                          ? `text-${method.color}-400`
                          : "text-gray-400 group-hover:text-blue-400"
                      } transition-colors`}
                    />
                    <span
                      className={`text-xs ${
                        isSelected
                          ? "text-gray-200"
                          : "text-gray-500"
                      }`}
                    >
                      {method.label}
                    </span>
                  </div>

                  {isSelected && (
                    <div className="absolute top-1 right-1 w-2 h-2 rounded-full bg-blue-400 shadow-lg shadow-blue-400/50" />
                  )}
                </button>
              );
            })}
          </div>
        </div>

        {/* Auth details input */}
        <div className="space-y-2">
          <label className="text-sm text-gray-400">
            {selectedAuth === "oauth" && "Client ID & Secret"}
            {selectedAuth === "token" && "Bearer Token"}
            {selectedAuth === "apikey" && "API Key"}
          </label>

          {selectedAuth === "oauth" ? (
            <div className="space-y-2">
              <input
                type="text"
                placeholder="Client ID"
                value={clientId}
                onChange={(e) => setClientId(e.target.value)}
                className="w-full h-10 px-4 bg-gray-900/50 border border-blue-500/20 rounded-lg text-sm text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-400/50 focus:bg-gray-900/70 transition-all font-mono"
              />
              <input
                type="password"
                placeholder="Client Secret"
                value={clientSecret}
                onChange={(e) => setClientSecret(e.target.value)}
                className="w-full h-10 px-4 bg-gray-900/50 border border-blue-500/20 rounded-lg text-sm text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-400/50 focus:bg-gray-900/70 transition-all font-mono"
              />
            </div>
          ) : (
            <input
              type="password"
              placeholder={`Enter ${selectedAuth === "token" ? "token" : "API key"}...`}
              value={tokenOrKey}
              onChange={(e) => setTokenOrKey(e.target.value)}
              className="w-full h-10 px-4 bg-gray-900/50 border border-blue-500/20 rounded-lg text-sm text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-400/50 focus:bg-gray-900/70 transition-all font-mono"
            />
          )}
        </div>

        {/* Endpoint URL */}
        <div className="space-y-2">
          <label className="text-sm text-gray-400">
            Endpoint URL
          </label>
          <input
            type="url"
            placeholder="https://api.example.com"
            value={endpointUrl}
            onChange={(e) => setEndpointUrl(e.target.value)}
            className="w-full h-10 px-4 bg-gray-900/50 border border-blue-500/20 rounded-lg text-sm text-gray-300 placeholder-gray-600 focus:outline-none focus:border-blue-400/50 focus:bg-gray-900/70 transition-all font-mono"
          />
        </div>

        {/* Action buttons */}
        <div className="flex space-x-3 pt-2">
          <button
            className="flex-1 h-10 rounded-lg bg-gradient-to-r from-blue-500 to-violet-500 hover:from-blue-400 hover:to-violet-400 text-white font-medium text-sm transition-all shadow-lg shadow-blue-500/30 hover:shadow-blue-500/50"
            onClick={() => {
              postToHost("api.addCustomIntegration", {
                name,
                auth: selectedAuth,
                clientId,
                clientSecret,
                tokenOrKey,
                endpointUrl,
              });
              postToHost("api.getState");
            }}
          >
            Connect
          </button>
          <button
            className="h-10 px-4 rounded-lg border border-blue-500/20 bg-gray-900/30 hover:border-blue-400/30 hover:bg-gray-900/50 text-gray-400 hover:text-gray-300 text-sm transition-all"
            onClick={() => {
              postToHost("api.test", {
                auth: selectedAuth,
                clientId,
                clientSecret,
                tokenOrKey,
                endpointUrl,
              });
            }}
          >
            Test
          </button>
        </div>
      </div>
    </div>
  );
}
