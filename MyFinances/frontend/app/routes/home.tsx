import { useEffect, useState } from "react";
import { redirect, useNavigate } from "react-router";
import type { Route } from "./+types/home";
import { Welcome } from "../welcome/welcome";
import { apiFetch } from "../lib/api";

export function meta({}: Route.MetaArgs) {
  return [
    { title: "New React Router App" },
    { name: "description", content: "Welcome to React Router!" },
  ];
}

export async function clientLoader() {
  const res = await fetch("/api/auth/me");
  if (!res.ok) throw redirect("/login");
  return res.json();
}

interface WeatherForecast {
  date: string;
  temperatureC: number;
  summary: string | null;
  temperatureF: number;
}

function WeatherForecastTable() {
  const [forecast, setForecast] = useState<WeatherForecast[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiFetch<WeatherForecast[]>("/weatherforecast")
      .then(setForecast)
      .catch((err: Error) => setError(err.message));
  }, []);

  if (error) {
    return (
      <p className="text-center text-sm text-red-600">
        API call failed: {error}
      </p>
    );
  }

  if (!forecast) {
    return <p className="text-center text-sm text-gray-500">Loading forecast…</p>;
  }

  return (
    <table className="mx-auto text-sm">
      <thead>
        <tr className="text-left text-gray-500">
          <th className="px-3 py-1">Date</th>
          <th className="px-3 py-1">°C</th>
          <th className="px-3 py-1">°F</th>
          <th className="px-3 py-1">Summary</th>
        </tr>
      </thead>
      <tbody>
        {forecast.map((day) => (
          <tr key={day.date}>
            <td className="px-3 py-1">{day.date}</td>
            <td className="px-3 py-1">{day.temperatureC}</td>
            <td className="px-3 py-1">{day.temperatureF}</td>
            <td className="px-3 py-1">{day.summary}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export default function Home() {
  const navigate = useNavigate();

  async function handleLogout() {
    await apiFetch("/auth/logout", { method: "POST" });
    navigate("/login");
  }

  return (
    <>
      <div className="flex justify-end p-4">
        <button
          type="button"
          onClick={handleLogout}
          className="text-sm text-blue-700 hover:underline dark:text-blue-500"
        >
          Log out
        </button>
      </div>
      <WeatherForecastTable />
      <Welcome />
    </>
  );
}
