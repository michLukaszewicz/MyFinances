import type { Transaction } from "../../../Models/Transaction";
import Box from "../../Box/Box";
import { Cell, Pie, PieChart, ResponsiveContainer } from "recharts";

type Props = {
  history: Transaction[];
};

const COLORS = ["#0088FE", "#00C49F", "#FFBB28", "#FF8042", "#FF00FF", "#FF4500", "#FFD700", "#ADFF2F", "#7FFF00", "#00FFFF"];

const CategoryChart = ({ history }: Props) => {
  const total = history.reduce((acc, transaction) => acc + Math.abs(transaction.amount), 0);
  const totalByCategory = history.reduce(
    (acc, transaction) => {
      const category = transaction.category;
      if (!acc[category]) {
        acc[category] = 0;
      }
      acc[category] += Math.abs(transaction.amount);
      return acc;
    },
    {} as Record<string, number>
  );
  const percentageByCategory: Record<string, string> = Object.entries(totalByCategory).reduce(
    (acc, [category, categoryValue]) => {
      const percent = ((categoryValue / total) * 100).toFixed(1); // np. 66.7
      acc[category] = `${percent}%`;
      return acc;
    },
    {} as Record<string, string>
  );

  const data = Object.entries(totalByCategory).map(([category, value]) => ({
    name: category,
    value: value,
    label: `${category} (${percentageByCategory[category]})`,
  }));

  return (
    <Box>
      <div className="flex justify-between mb-3">
        <p className="font-bold text-2xl mb-3">Category Chart</p>
        <div>
          {/* dodać tutaj możliwość wyłączeina wybranych kategorii i wybranie okresu  */}
          <p className="text-blue-600 text-sm text-right">Settings</p>
        </div>
      </div>
      <div className="flex flex-col items-center">
        <ResponsiveContainer maxHeight={250} width="95%" aspect={4.0 / 3.0} className="mx-4 mr-15">
          <PieChart width={400} height={400}>
            <Pie data={data} labelLine={true} label={({ index }) => data[index].label} outerRadius={80} dataKey="value">
              {data.map((entry, index) => (
                <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
              ))}
            </Pie>
          </PieChart>
        </ResponsiveContainer>
      </div>
    </Box>
  );
};

export default CategoryChart;
