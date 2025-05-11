import {
  Bar,
  BarChart,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";
import { useState } from "react";

enum TimePeriod {
  LastMonth = "Last Month",
  LastThreeMonths = "Last 3 Months",
  LastSixMonths = "Last 6 Months",
  LastYear = "Last Year",
}

type Props = {
  history: Transaction[];
};

const BalanceChart = ({ history }: Props) => {
  const [timePeriod, setTimePeriod] = useState<TimePeriod>(
    TimePeriod.LastMonth
  );
  
  const now = new Date();
  // To powinno być w jakiejś osobnej klasie ale chyba i tak będzie w api
  const lastMonthName: string = history[0].date.toLocaleString('default', { month: 'long' })
  const income = history.filter((item) => item.date.getMonth() === now.getMonth() && item.date.getFullYear() === now.getFullYear())
  .filter((item) => item.amount > 0).reduce((sum, item) => sum + item.amount, 0);
    const expencess = history.filter((item) => item.date.getMonth() === now.getMonth() && item.date.getFullYear() === now.getFullYear())
  .filter((item) => item.amount < 0).reduce((sum, item) => sum + -item.amount, 0);

  const data = [{ name: lastMonthName, expenses: expencess, income: income }];

  return (
    <Box>
      <div className="flex justify-between mb-3">
        <p className="font-bold text-2xl mb-3">Balance Chart</p>
        <div>
          <p className="text-gray-500 text-xs">Select Time Period</p>
          <p className="text-blue-600 text-sm text-right">{timePeriod}</p>
        </div>
      </div>
      <div className="flex flex-col items-center">
        <ResponsiveContainer
          maxHeight={150}
          width="95%"
          aspect={4.0 / 3.0}
          className="mx-4 mr-15"
        >
          <BarChart width={1000} height={100} data={data} layout="vertical">
            <YAxis type="category" dataKey="name" />
            <XAxis type="number" />
            <Legend />
            <Tooltip />
            <Bar dataKey="expenses" fill="#c10007" />
            <Bar dataKey="income" fill="#00a63e" />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </Box>
  );
};

export default BalanceChart;
