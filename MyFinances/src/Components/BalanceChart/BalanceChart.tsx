import {
  Bar,
  BarChart,
  Legend,
  ResponsiveContainer,
  XAxis,
  YAxis,
} from "recharts";
import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";

type Props = {
  history: Transaction[];
};

const BalanceChart = ({ history }: Props) => {
  const data = [{ name: "May", expenses: 4000, income: 5200 }];

  return (
    <Box>
      <div className="flex justify-between mb-3">
        <p className="font-bold text-2xl mb-3">Balance Chart</p>
        <div>
          <p className="text-gray-500 text-xs">Select Time Period</p>
          <p className="text-blue-600 text-sm text-right">Last Month</p>
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
            <Bar dataKey="expenses" fill="#c10007" />
            <Bar dataKey="income" fill="#00a63e" />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </Box>
  );
};

export default BalanceChart;
